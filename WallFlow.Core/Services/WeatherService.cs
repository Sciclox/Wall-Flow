using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WallFlow.Services
{
    public class ForecastDay
    {
        public string DayName { get; set; } = string.Empty;
        public string Glyph { get; set; } = "\uE706";
        public string TempRange { get; set; } = "--° / --°";
        public string MinTemp { get; set; } = "--";
        public string MaxTemp { get; set; } = "--";
        public string Condition { get; set; } = "Despejado";
    }

    public class WeatherData
    {
        public double Temperature { get; set; }
        public double ApparentTemperature { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public double Pressure { get; set; } = 1013;
        public int CloudCover { get; set; } = 0;
        public string Condition { get; set; } = "Despejado";
        public string Glyph { get; set; } = "\uE706";
        public string City { get; set; } = "Lima";
        public List<ForecastDay> Forecast { get; set; } = new();
    }

    public class WeatherService
    {
        private static WeatherService? _instance;
        public static WeatherService Instance => _instance ??= new WeatherService();

        private readonly HttpClient _client;
        private WeatherData? _cachedData;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15);

        private WeatherService()
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(3);
            // Set User-Agent to avoid API blocks
            _client.DefaultRequestHeaders.Add("User-Agent", "WallFlow/1.0");
        }

        public void ResetCache()
        {
            _cachedData = null;
            _lastFetchTime = DateTime.MinValue;
        }

        public async Task<WeatherData> GetWeatherAsync()
        {
            if (_cachedData != null && DateTime.Now - _lastFetchTime < _cacheDuration)
            {
                return _cachedData;
            }

            try
            {
                string city = SettingsService.Instance.WeatherCity;
                double lat = -11.8489; // Default (Lima Norte, Peru)
                double lon = -77.0060;
                bool hasCustomCity = !string.IsNullOrEmpty(city);

                if (hasCustomCity)
                {
                    if (TryParseCoordinates(city, out var customLat, out var customLon))
                    {
                        lat = customLat;
                        lon = customLon;
                        hasCustomCity = false; // Route to coordinate fetching
                    }
                }

                if (!hasCustomCity && string.IsNullOrEmpty(SettingsService.Instance.WeatherCity))
                {
                    // 1. Geolocate user by IP using ipinfo.io (fast, reliable, supports HTTPS)
                    try
                    {
                        var geoResponse = await _client.GetStringAsync("https://ipinfo.io/json");
                        using (var doc = JsonDocument.Parse(geoResponse))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("loc", out var locProp))
                            {
                                var locStr = locProp.GetString() ?? "";
                                var parts = locStr.Split(',');
                                if (parts.Length == 2)
                                {
                                    lat = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                                    lon = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                                }
                            }
                            if (root.TryGetProperty("city", out var cityProp))
                            {
                                city = cityProp.GetString() ?? "Lima";
                            }

                        }
                    }
                    catch { /* Fallback to default city */ }
                }

                // 2. Fetch weather forecast
                WeatherData data;
                try
                {
                    if (hasCustomCity)
                    {
                        data = await FetchWeatherFromWttrInAsync(city);
                        _cachedData = data;
                        _lastFetchTime = DateTime.Now;
                        return data;
                    }

                    // Try Open-Meteo first
                    string latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string lonStr = lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,surface_pressure,cloud_cover&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4";
                    var weatherResponse = await _client.GetStringAsync(weatherUrl);

                    data = new WeatherData { City = city };

                    using (var doc = JsonDocument.Parse(weatherResponse))
                    {
                        var root = doc.RootElement;

                        // Parse current conditions
                        if (root.TryGetProperty("current", out var current))
                        {
                            data.Temperature = current.GetProperty("temperature_2m").GetDouble();
                            data.ApparentTemperature = current.GetProperty("apparent_temperature").GetDouble();
                            data.Humidity = current.GetProperty("relative_humidity_2m").GetInt32();
                            data.WindSpeed = current.GetProperty("wind_speed_10m").GetDouble();
                            data.Pressure = current.GetProperty("surface_pressure").GetDouble();
                            data.CloudCover = current.GetProperty("cloud_cover").GetInt32();
                            
                            int code = current.GetProperty("weather_code").GetInt32();
                            int isDay = current.GetProperty("is_day").GetInt32();

                            data.Condition = MapWeatherCode(code);
                            data.Glyph = MapWeatherGlyph(code, isDay == 1);
                        }

                        // Parse daily forecast (days 0, 1, 2)
                        if (root.TryGetProperty("daily", out var daily))
                        {
                            var times = daily.GetProperty("time");
                            var codes = daily.GetProperty("weather_code");
                            var maxTemps = daily.GetProperty("temperature_2m_max");
                            var minTemps = daily.GetProperty("temperature_2m_min");

                            int length = times.GetArrayLength();
                            for (int i = 0; i < length && i < 3; i++)
                            {
                                string dateStr = times[i].GetString() ?? "";
                                string dayName;
                                if (i == 0)
                                {
                                    dayName = "Hoy";
                                }
                                else if (i == 1)
                                {
                                    dayName = "Mañana";
                                }
                                else
                                {
                                    DateTime date = DateTime.TryParse(dateStr, out var parsedDate) ? parsedDate : DateTime.Today.AddDays(i);
                                    dayName = date.ToString("dddd d", new System.Globalization.CultureInfo("es-ES"));
                                    if (dayName.Length > 0)
                                    {
                                        dayName = char.ToUpper(dayName[0]) + dayName.Substring(1);
                                    }
                                }

                                int code = codes[i].GetInt32();
                                double maxTemp = maxTemps[i].GetDouble();
                                double minTemp = minTemps[i].GetDouble();

                                data.Forecast.Add(new ForecastDay
                                {
                                    DayName = dayName,
                                    Glyph = MapWeatherGlyph(code, true),
                                    TempRange = $"{Math.Round(minTemp)}° / {Math.Round(maxTemp)}°",
                                    MinTemp = $"{Math.Round(minTemp)}°C",
                                    MaxTemp = $"{Math.Round(maxTemp)}°C",
                                    Condition = MapWeatherCode(code)
                                });
                            }
                        }
                    }

                    _cachedData = data;
                    _lastFetchTime = DateTime.Now;
                    return data;
                }
                catch
                {
                    // Open-Meteo failed/blocked, fallback to wttr.in backup weather API
                    data = await FetchWeatherFromWttrInAsync(city);
                    _cachedData = data;
                    _lastFetchTime = DateTime.Now;
                    return data;
                }
            }
            catch
            {
                // Both Open-Meteo and wttr.in failed, return fallback
                if (_cachedData != null) return _cachedData;

                var fallback = new WeatherData
                {
                    City = "Sin Conexión",
                    Condition = "Despejado",
                    Glyph = "\uE706",
                    Temperature = 20,
                    ApparentTemperature = 20,
                    Humidity = 65,
                    WindSpeed = 10,
                    Pressure = 1013,
                    CloudCover = 25
                };
                string day2Name = DateTime.Today.AddDays(2).ToString("dddd d", new System.Globalization.CultureInfo("es-ES"));
                if (day2Name.Length > 0) day2Name = char.ToUpper(day2Name[0]) + day2Name.Substring(1);

                fallback.Forecast.Add(new ForecastDay { DayName = "Hoy", Glyph = "\uE706", TempRange = "15° / 20°", MinTemp = "15°C", MaxTemp = "20°C", Condition = "Despejado" });
                fallback.Forecast.Add(new ForecastDay { DayName = "Mañana", Glyph = "\uE707", TempRange = "15° / 19°", MinTemp = "15°C", MaxTemp = "19°C", Condition = "Nublado" });
                fallback.Forecast.Add(new ForecastDay { DayName = day2Name, Glyph = "\uE774", TempRange = "15° / 19°", MinTemp = "15°C", MaxTemp = "19°C", Condition = "Lluvia" });
                return fallback;
            }
        }

        private async Task<WeatherData> FetchWeatherFromWttrInAsync(string city)
        {
            string url = $"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1";
            var response = await _client.GetStringAsync(url);

            var data = new WeatherData { City = city };

            using (var doc = JsonDocument.Parse(response))
            {
                var root = doc.RootElement;

                // Current condition
                if (root.TryGetProperty("current_condition", out var currentArr) && currentArr.GetArrayLength() > 0)
                {
                    var current = currentArr[0];
                    if (current.TryGetProperty("temp_C", out var tempProp))
                        data.Temperature = double.Parse(tempProp.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    if (current.TryGetProperty("FeelsLikeC", out var feelsProp))
                        data.ApparentTemperature = double.Parse(feelsProp.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    if (current.TryGetProperty("humidity", out var humProp))
                        data.Humidity = int.Parse(humProp.GetString() ?? "0");
                    if (current.TryGetProperty("windspeedKmph", out var windProp))
                        data.WindSpeed = double.Parse(windProp.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    if (current.TryGetProperty("pressure", out var presProp))
                        data.Pressure = double.Parse(presProp.GetString() ?? "1013", System.Globalization.CultureInfo.InvariantCulture);
                    if (current.TryGetProperty("cloudcover", out var cloudProp))
                        data.CloudCover = int.Parse(cloudProp.GetString() ?? "0");

                    string codeStr = current.TryGetProperty("weatherCode", out var codeProp) ? codeProp.GetString() ?? "113" : "113";
                    int wwoCode = int.TryParse(codeStr, out var val) ? val : 113;
                    
                    data.Condition = MapWwoCodeToCondition(wwoCode);
                    
                    bool isDay = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18;
                    data.Glyph = MapWwoCodeToGlyph(wwoCode, isDay);
                }

                // Forecast (days 0, 1, 2)
                if (root.TryGetProperty("weather", out var forecastArr))
                {
                    int length = forecastArr.GetArrayLength();
                    for (int i = 0; i < length && i < 3; i++)
                    {
                        var day = forecastArr[i];
                        string dateStr = day.GetProperty("date").GetString() ?? "";
                        string dayName;
                        if (i == 0)
                        {
                            dayName = "Hoy";
                        }
                        else if (i == 1)
                        {
                            dayName = "Mañana";
                        }
                        else
                        {
                            DateTime date = DateTime.TryParse(dateStr, out var parsedDate) ? parsedDate : DateTime.Today.AddDays(i);
                            dayName = date.ToString("dddd d", new System.Globalization.CultureInfo("es-ES"));
                            if (dayName.Length > 0)
                            {
                                dayName = char.ToUpper(dayName[0]) + dayName.Substring(1);
                            }
                        }

                        string maxTempStr = day.GetProperty("maxtempC").GetString() ?? "0";
                        string minTempStr = day.GetProperty("mintempC").GetString() ?? "0";
                        double maxTemp = double.Parse(maxTempStr, System.Globalization.CultureInfo.InvariantCulture);
                        double minTemp = double.Parse(minTempStr, System.Globalization.CultureInfo.InvariantCulture);

                        int wwoCode = 113;
                        if (day.TryGetProperty("hourly", out var hourly) && hourly.GetArrayLength() > 4)
                        {
                            string codeStr = hourly[4].GetProperty("weatherCode").GetString() ?? "113";
                            wwoCode = int.TryParse(codeStr, out var val) ? val : 113;
                        }

                        data.Forecast.Add(new ForecastDay
                        {
                            DayName = dayName,
                            Glyph = MapWwoCodeToGlyph(wwoCode, true),
                            TempRange = $"{Math.Round(minTemp)}° / {Math.Round(maxTemp)}°",
                            MinTemp = $"{Math.Round(minTemp)}°C",
                            MaxTemp = $"{Math.Round(maxTemp)}°C",
                            Condition = MapWwoCodeToCondition(wwoCode)
                        });
                    }
                }
            }

            return data;
        }

        private static string MapWwoCodeToCondition(int code)
        {
            return code switch
            {
                113 => "Despejado",
                116 => "Parcialmente Nublado",
                119 => "Nublado",
                122 => "Cubierto",
                143 or 248 or 260 => "Niebla / Neblina",
                176 or 293 or 296 or 299 or 302 or 353 or 356 or 359 => "Lluvia",
                386 or 389 or 392 or 395 => "Tormenta",
                182 or 185 or 281 or 284 or 311 or 314 or 317 or 320 => "Lluvia gélida",
                227 or 230 or 323 or 326 or 329 or 332 or 335 or 338 or 368 or 371 => "Nieve",
                _ => "Despejado"
            };
        }

        private static string MapWwoCodeToGlyph(int code, bool isDay)
        {
            return code switch
            {
                113 => isDay ? "\uE706" : "\uE708", // Sun / Moon
                116 => isDay ? "\uE707" : "\uE709", // Sun with cloud / Moon with cloud
                119 or 122 => "\uE753", // Cloudy
                143 or 248 or 260 => "\uE752", // Fog
                176 or 293 or 296 or 299 or 302 or 353 or 356 or 359 => "\uE774", // Rain
                386 or 389 or 392 or 395 => "\uE7C8", // Thunderstorm
                _ => isDay ? "\uE706" : "\uE708"
            };
        }

        public static string MapWeatherCode(int code)
        {
            return code switch
            {
                0 => "Despejado",
                1 => "Mayormente Despejado",
                2 => "Parcialmente Nublado",
                3 => "Cubierto",
                45 or 48 => "Niebla",
                51 or 53 or 55 => "Llovizna",
                56 or 57 => "Llovizna Helada",
                61 or 63 or 65 => "Lluvia",
                66 or 67 => "Lluvia Helada",
                71 or 73 or 75 => "Nieve",
                77 => "Granizo de Nieve",
                80 or 81 or 82 => "Chubascos de Lluvia",
                85 or 86 => "Chubascos de Nieve",
                95 => "Tormenta",
                96 or 99 => "Tormenta Fuerte",
                _ => "Desconocido"
            };
        }

        public static string MapWeatherGlyph(int code, bool isDay)
        {
            return code switch
            {
                0 => isDay ? "\uE706" : "\uE708", // Sun or Moon
                1 or 2 => isDay ? "\uE707" : "\uE709", // Cloudy Sun or Cloudy Moon
                3 => "\uE753", // Cloudy
                45 or 48 => "\uE752", // Foggy
                51 or 53 or 55 or 56 or 57 => "\uE775", // Light Rain/Drizzle
                61 or 63 or 65 or 66 or 67 => "\uE774", // Heavy Rain
                71 or 73 or 75 or 77 => "\uE781", // Snowy
                80 or 81 or 82 or 85 or 86 => "\uE717", // Showers
                95 or 96 or 99 => "\uE7C8", // Thunderstorm
                _ => "\uE706"
            };
        }

        public static bool TryParseCoordinates(string input, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;
            if (string.IsNullOrEmpty(input)) return false;

            // Normalize quotes and spaces
            input = input.Replace("“", "\"").Replace("”", "\"").Replace("'", "'").Trim();

            // 1. Try Decimal Coordinates (e.g. "-11.8489,-77.0060")
            var parts = input.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedLat) &&
                    double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedLon))
                {
                    lat = parsedLat;
                    lon = parsedLon;
                    return true;
                }
            }

            // 2. Try DMS Coordinates (e.g. 11°50'56.1"S 77°00'21.5"W)
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, 
                    @"(\d+)[°\s]+(\d+)['\s]+([\d\.]+)[""\s]+([NSns])\s+(\d+)[°\s]+(\d+)['\s]+([\d\.]+)[""\s]+([EWew])");
                if (match.Success)
                {
                    double latDeg = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double latMin = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double latSec = double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string latDir = match.Groups[4].Value.ToUpper();

                    double lonDeg = double.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double lonMin = double.Parse(match.Groups[6].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double lonSec = double.Parse(match.Groups[7].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string lonDir = match.Groups[8].Value.ToUpper();

                    lat = latDeg + (latMin / 60.0) + (latSec / 3600.0);
                    if (latDir == "S") lat = -lat;

                    lon = lonDeg + (lonMin / 60.0) + (lonSec / 3600.0);
                    if (lonDir == "W") lon = -lon;

                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
