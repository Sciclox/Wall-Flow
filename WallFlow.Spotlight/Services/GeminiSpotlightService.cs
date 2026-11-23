using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WallFlow.Spotlight.Services;

public interface IGeminiSpotlightService
{
    Task<string> QueryAsync(string prompt, string apiKey, string? preferredModel = null, CancellationToken cancellationToken = default);
    Task<string> QueryChatAsync(IEnumerable<(string role, string text)> history, string apiKey, string? preferredModel = null, CancellationToken cancellationToken = default);
    string CleanAiResponse(string rawResponse);
}

/// <summary>
/// Service that interfaces with the Google Gemini API to provide fast, direct AI answers in Spotlight.
/// </summary>
public class GeminiSpotlightService : IGeminiSpotlightService
{
    private static GeminiSpotlightService? _instance;
    public static GeminiSpotlightService Instance => _instance ??= new GeminiSpotlightService();

    private readonly HttpClient _httpClient;

    public GeminiSpotlightService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> QueryAsync(string prompt, string apiKey, string? preferredModel = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("El prompt no puede estar vacío.", nameof(prompt));

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API Key de Gemini no configurada.");

        string primaryModel = string.IsNullOrWhiteSpace(preferredModel)
            ? "gemini-3.5-flash-lite"
            : preferredModel;

        var modelsToTry = new List<string>
        {
            primaryModel,
            "gemini-3.5-flash-lite",
            "gemini-3.6-flash",
            "gemini-3.5-flash",
            "gemini-3.7-flash",
            "gemini-3.8-flash"
        };
        var uniqueModels = modelsToTry.Distinct().ToArray();

        string webSnippets = await FetchWebSearchResultsAsync(prompt, cancellationToken);
        string currentDateStr = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES"));
        string systemText = $"Actúa como un asistente directo. La fecha y año actual es {currentDateStr}. Proporciona la respuesta final en español de forma directa, concisa y rápida, sin introducciones ni rodeos.";
        if (!string.IsNullOrWhiteSpace(webSnippets))
        {
            systemText += $"\n\n[Resultados de búsqueda web en tiempo real]:\n{webSnippets}\n\nInstrucción: Utiliza la información de búsqueda web más reciente anterior para responder con datos certeros y actualizados al día de hoy.";
        }

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new { text = systemText }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            toolConfig = new
            {
                functionCallingConfig = new
                {
                    mode = "NONE"
                }
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_CIVIC_INTEGRITY", threshold = "BLOCK_NONE" }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024
            }
        };

        string jsonContent = JsonSerializer.Serialize(requestBody);
        HttpResponseMessage? response = null;
        string responseJson = string.Empty;
        string lastErrDetail = string.Empty;

        foreach (var model in uniqueModels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                response = await _httpClient.PostAsync(url, content, cancellationToken);
                responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var docCheck = JsonDocument.Parse(responseJson);
                        if (docCheck.RootElement.TryGetProperty("candidates", out var cands) &&
                            cands.ValueKind == JsonValueKind.Array &&
                            cands.GetArrayLength() > 0)
                        {
                            var firstCand = cands[0];
                            bool isBadFinish = false;
                            if (firstCand.TryGetProperty("finishReason", out var frProp))
                            {
                                string fr = frProp.GetString() ?? string.Empty;
                                if (fr == "MALFORMED_FUNCTION_CALL" || fr == "SAFETY")
                                {
                                    isBadFinish = true;
                                }
                            }

                            if (!isBadFinish &&
                                firstCand.TryGetProperty("content", out var cObj) &&
                                cObj.TryGetProperty("parts", out var pArr) &&
                                pArr.ValueKind == JsonValueKind.Array &&
                                pArr.GetArrayLength() > 0)
                            {
                                break;
                            }
                        }
                    }
                    catch { /* retry next model if parse error */ }
                }

                try
                {
                    using var docErr = JsonDocument.Parse(responseJson);
                    if (docErr.RootElement.TryGetProperty("error", out var errObj) &&
                        errObj.TryGetProperty("message", out var msgProp))
                    {
                        lastErrDetail = msgProp.GetString() ?? string.Empty;
                    }
                }
                catch { /* ignored */ }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastErrDetail = ex.Message;
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            string errMessage = !string.IsNullOrWhiteSpace(lastErrDetail)
                ? lastErrDetail
                : (response != null ? $"Error HTTP {response.StatusCode}" : "Error de conexión");
            throw new HttpRequestException(errMessage);
        }

        using var doc = JsonDocument.Parse(responseJson);
        string aiAnswer = string.Empty;

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                contentObj.TryGetProperty("parts", out var partsArr) &&
                partsArr.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in partsArr.EnumerateArray())
                {
                    // Skip internal chain-of-thought blocks ("thought": true)
                    if (part.TryGetProperty("thought", out var thoughtElem) && thoughtElem.GetBoolean())
                    {
                        continue;
                    }

                    if (part.TryGetProperty("text", out var textElem))
                    {
                        sb.Append(textElem.GetString() ?? string.Empty);
                    }
                }
                aiAnswer = sb.ToString();
            }

            if (string.IsNullOrWhiteSpace(aiAnswer) && firstCandidate.TryGetProperty("finishReason", out var finishReasonProp))
            {
                string fr = finishReasonProp.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(fr) && fr != "STOP")
                {
                    aiAnswer = $"*Respuesta filtrada por Gemini ({fr}).*";
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("promptFeedback", out var promptFeedback) &&
                 promptFeedback.TryGetProperty("blockReason", out var blockReason))
        {
            string br = blockReason.GetString() ?? "Seguridad";
            aiAnswer = $"*Consulta bloqueada por filtro de seguridad ({br}).*";
        }

        if (string.IsNullOrWhiteSpace(aiAnswer))
        {
            aiAnswer = "No se obtuvo respuesta de la IA.";
        }

        return CleanAiResponse(aiAnswer);
    }

    public async Task<string> QueryChatAsync(IEnumerable<(string role, string text)> history, string apiKey, string? preferredModel = null, CancellationToken cancellationToken = default)
    {
        if (history == null || !history.Any())
            throw new ArgumentException("El historial de conversación no puede estar vacío.", nameof(history));

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API Key de Gemini no configurada.");

        string primaryModel = string.IsNullOrWhiteSpace(preferredModel)
            ? "gemini-3.5-flash-lite"
            : preferredModel;

        var modelsToTry = new List<string>
        {
            primaryModel,
            "gemini-3.5-flash-lite",
            "gemini-3.6-flash",
            "gemini-3.5-flash",
            "gemini-3.7-flash",
            "gemini-3.8-flash"
        };
        var uniqueModels = modelsToTry.Distinct().ToArray();

        var contentsList = new List<object>();
        foreach (var (role, text) in history)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            string normalizedRole = role.ToLowerInvariant() switch
            {
                "user" => "user",
                "model" or "assistant" or "ia" => "model",
                _ => "user"
            };

            contentsList.Add(new
            {
                role = normalizedRole,
                parts = new[] { new { text = text.Trim() } }
            });
        }

        if (contentsList.Count == 0)
            throw new ArgumentException("No hay mensajes válidos en el historial.", nameof(history));

        string latestQuery = string.Empty;
        var lastUserEntry = history.LastOrDefault(x => x.role.Equals("user", StringComparison.OrdinalIgnoreCase) || x.role.Equals("tú", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(lastUserEntry.text))
        {
            latestQuery = lastUserEntry.text;
        }

        string webSnippets = await FetchWebSearchResultsAsync(latestQuery, cancellationToken);
        string chatDateStr = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy, HH:mm", new System.Globalization.CultureInfo("es-ES"));
        string systemText = $"Eres Gemini AI integrado en el Spotlight de WallFlow. La fecha y hora actual del sistema en tiempo real es {chatDateStr}. Ten siempre presente que hoy es {chatDateStr} y el año en curso es {DateTime.Now.Year}. Tienes acceso a resultados de búsqueda en internet en vivo para contestar siempre con datos actualizados.";
        if (!string.IsNullOrWhiteSpace(webSnippets))
        {
            systemText += $"\n\n[Resultados de búsqueda web en tiempo real]:\n{webSnippets}\n\nInstrucción: Utiliza la información de búsqueda web más reciente anterior para proporcionar una respuesta precisa, certera y actualizada a fecha de hoy.";
        }
        systemText += " Responde en español de forma útil, elegante, directa y amigable. Tienes soporte completo para formato Markdown (títulos, negrita, listas ordenadas y con viñetas, bloques de código, tablas y citas). No inventes información técnica y mantén respuestas bien estructuradas.";

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new { text = systemText }
                }
            },
            contents = contentsList,
            toolConfig = new
            {
                functionCallingConfig = new
                {
                    mode = "NONE"
                }
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_CIVIC_INTEGRITY", threshold = "BLOCK_NONE" }
            },
            generationConfig = new
            {
                temperature = 0.4,
                maxOutputTokens = 2048
            }
        };

        string jsonContent = JsonSerializer.Serialize(requestBody);
        HttpResponseMessage? response = null;
        string responseJson = string.Empty;
        string lastErrDetail = string.Empty;

        foreach (var model in uniqueModels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                response = await _httpClient.PostAsync(url, content, cancellationToken);
                responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var docCheck = JsonDocument.Parse(responseJson);
                        if (docCheck.RootElement.TryGetProperty("candidates", out var cands) &&
                            cands.ValueKind == JsonValueKind.Array &&
                            cands.GetArrayLength() > 0)
                        {
                            var firstCand = cands[0];
                            bool isBadFinish = false;
                            if (firstCand.TryGetProperty("finishReason", out var frProp))
                            {
                                string fr = frProp.GetString() ?? string.Empty;
                                if (fr == "MALFORMED_FUNCTION_CALL" || fr == "SAFETY")
                                {
                                    isBadFinish = true;
                                }
                            }

                            if (!isBadFinish &&
                                firstCand.TryGetProperty("content", out var cObj) &&
                                cObj.TryGetProperty("parts", out var pArr) &&
                                pArr.ValueKind == JsonValueKind.Array &&
                                pArr.GetArrayLength() > 0)
                            {
                                break;
                            }
                        }
                    }
                    catch { /* retry next model if parse error */ }
                }

                try
                {
                    using var docErr = JsonDocument.Parse(responseJson);
                    if (docErr.RootElement.TryGetProperty("error", out var errObj) &&
                        errObj.TryGetProperty("message", out var msgProp))
                    {
                        lastErrDetail = msgProp.GetString() ?? string.Empty;
                    }
                }
                catch { /* ignored */ }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastErrDetail = ex.Message;
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            string errMessage = !string.IsNullOrWhiteSpace(lastErrDetail)
                ? lastErrDetail
                : (response != null ? $"Error HTTP {response.StatusCode}" : "Error de conexión con Gemini");
            throw new HttpRequestException(errMessage);
        }

        using var doc = JsonDocument.Parse(responseJson);
        string aiAnswer = string.Empty;

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                contentObj.TryGetProperty("parts", out var partsArr) &&
                partsArr.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in partsArr.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var thoughtElem) && thoughtElem.GetBoolean())
                    {
                        continue;
                    }

                    if (part.TryGetProperty("text", out var textElem))
                    {
                        sb.Append(textElem.GetString() ?? string.Empty);
                    }
                }
                aiAnswer = sb.ToString();
            }

            if (string.IsNullOrWhiteSpace(aiAnswer) && firstCandidate.TryGetProperty("finishReason", out var finishReasonProp))
            {
                string fr = finishReasonProp.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(fr) && fr != "STOP")
                {
                    aiAnswer = $"*Respuesta filtrada por políticas de Gemini ({fr}).*";
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("promptFeedback", out var promptFeedback) &&
                 promptFeedback.TryGetProperty("blockReason", out var blockReason))
        {
            string br = blockReason.GetString() ?? "Seguridad";
            aiAnswer = $"*Consulta bloqueada por filtro de seguridad ({br}).*";
        }

        if (string.IsNullOrWhiteSpace(aiAnswer))
        {
            aiAnswer = "No se obtuvo respuesta de la IA.";
        }

        return aiAnswer.Trim();
    }

    public string CleanAiResponse(string rawResponse)
    {
        string clean = rawResponse.Trim();

        // 1. Strip label prefixes like "Response:", "Greeting:", "Output:", "Answer:"
        int colonIdx = clean.IndexOf(':');
        if (colonIdx > 0 && colonIdx <= 12)
        {
            string prefix = clean.Substring(0, colonIdx).Trim();
            if (prefix.Equals("Response", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("Greeting", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("Answer", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("Result", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(colonIdx + 1).Trim();
            }
        }

        // 2. Handle repeated quoted patterns e.g. "Hola"Hola or "Text"
        if (clean.StartsWith("\""))
        {
            int lastQuoteIdx = clean.LastIndexOf('"');
            if (lastQuoteIdx > 0)
            {
                string afterQuote = clean.Substring(lastQuoteIdx + 1).Trim();
                if (!string.IsNullOrWhiteSpace(afterQuote))
                {
                    clean = afterQuote;
                }
                else
                {
                    clean = clean.Substring(1, lastQuoteIdx - 1).Trim();
                }
            }
        }

        // 3. Strip surrounding quotes or backticks
        return clean.Trim('"', '\'', ' ');
    }

    private async Task<string> FetchWebSearchResultsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
            return string.Empty;

        try
        {
            string url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query.Trim());
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var resp = await _httpClient.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return string.Empty;

            string html = await resp.Content.ReadAsStringAsync(cts.Token);
            var matches = Regex.Matches(html, @"class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            int count = 0;
            foreach (Match m in matches)
            {
                if (count >= 4) break;
                string clean = Regex.Replace(m.Groups[1].Value, "<.*?>", string.Empty);
                clean = System.Net.WebUtility.HtmlDecode(clean).Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    sb.AppendLine($"- {clean}");
                    count++;
                }
            }
            return sb.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
