using System;
using System.Linq;
using HidSharp;

namespace DeathAdderManager.Diagnostics;

public class DeviceScanner
{
    public static void Main()
    {
        Console.WriteLine("Scanning for Razer DeathAdder Essential HID interfaces...");
        var allRazer = DeviceList.Local.GetHidDevices(vendorID: 0x1532).ToList();
        
        if (!allRazer.Any())
        {
            Console.WriteLine("No Razer devices found.");
            return;
        }

        foreach (var dev in allRazer)
        {
            Console.WriteLine($"--------------------------------------------------");
            Console.WriteLine($"Path: {dev.DevicePath}");
            Console.WriteLine($"PID:  0x{dev.ProductID:X4}");
            
            // Using non-obsolete methods
            Console.WriteLine($"Max Input Len:   {dev.GetMaxInputReportLength()}");
            Console.WriteLine($"Max Output Len:  {dev.GetMaxOutputReportLength()}");
            Console.WriteLine($"Max Feature Len: {dev.GetMaxFeatureReportLength()}");
            
            try
            {
                var config = new OpenConfiguration();
                config.SetOption(OpenOption.Exclusive, false);
                using var stream = dev.Open(config);
                Console.WriteLine("Status: OPEN SUCCESS (Non-Exclusive)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Status: OPEN FAILED - {ex.Message}");
            }
        }
    }
}
