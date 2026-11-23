using System;
using System.Linq;
using System.Threading.Tasks;
using WallFlow.Windows;
using Xunit;
using Xunit.Abstractions;

namespace WallFlow.Tests;

public class ConnectedDevicesDiscoveryTests
{
    private readonly ITestOutputHelper _output;

    public ConnectedDevicesDiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SpotlightConnectedDeviceItem_PropertiesWorkAsExpected()
    {
        var item = new SpotlightConnectedDeviceItem
        {
            Id = "test-device-1",
            Name = "AK820 5.1-1",
            Category = "Teclado",
            Glyph = "⌨️",
            StatusText = "Conectado",
            DetailsText = "Bluetooth LE",
            IsConnected = true,
            BatteryText = "85%"
        };

        Assert.Equal("test-device-1", item.Id);
        Assert.Equal("AK820 5.1-1", item.Name);
        Assert.Equal("Teclado", item.Category);
        Assert.Equal("⌨️", item.Glyph);
        Assert.True(item.IsConnected);
        Assert.True(item.HasBattery);
        Assert.True(item.HasDetails);
        Assert.False(item.IsDefault);
    }

    [Fact]
    public async Task EnumerateConnectedDevices_AudioAndBluetooth_SmokeTest()
    {
        // 1. Audio Render Devices
        var audioRenderSelector = global::Windows.Media.Devices.MediaDevice.GetAudioRenderSelector();
        var audioRenderDevices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(audioRenderSelector);
        Assert.NotNull(audioRenderDevices);

        // 2. Audio Capture Devices
        var audioCaptureSelector = global::Windows.Media.Devices.MediaDevice.GetAudioCaptureSelector();
        var audioCaptureDevices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(audioCaptureSelector);
        Assert.NotNull(audioCaptureDevices);

        // 3. Bluetooth Classic Connected
        var btSelector = global::Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromConnectionStatus(global::Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected);
        var btDevices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(btSelector);
        Assert.NotNull(btDevices);

        // 4. Bluetooth LE Connected
        var btLeSelector = global::Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(global::Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected);
        var btLeDevices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(btLeSelector);
        Assert.NotNull(btLeDevices);

        // 5. Gamepads & RawGameControllers
        var rawControllers = global::Windows.Gaming.Input.RawGameController.RawGameControllers;
        Assert.NotNull(rawControllers);

        // 6. Removable Drives
        var drives = System.IO.DriveInfo.GetDrives();
        Assert.NotNull(drives);
    }
}
