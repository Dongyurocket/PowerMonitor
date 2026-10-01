using System.Text;
using LibreHardwareMonitor.Hardware;

namespace PowerMonitor;

public record PowerItem(string Key, string Name, string Kind, float Watts);

/// <summary>Wraps LibreHardwareMonitor and picks one representative power value per hardware device.</summary>
public sealed class PowerReader : IDisposable
{
    private readonly Computer _pc = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsPsuEnabled = true,
        IsBatteryEnabled = true,
        IsControllerEnabled = true,
    };

    public PowerReader() => _pc.Open();

    public void Update()
    {
        foreach (var hw in _pc.Hardware) UpdateRecursive(hw);
    }

    private static void UpdateRecursive(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware) UpdateRecursive(sub);
    }

    private static IEnumerable<IHardware> All(IEnumerable<IHardware> list)
    {
        foreach (var hw in list)
        {
            yield return hw;
            foreach (var s in All(hw.SubHardware)) yield return s;
        }
    }

    /// <summary>One entry per device that exposes power sensors.</summary>
    public List<PowerItem> Read()
    {
        var items = new List<PowerItem>();
        foreach (var hw in All(_pc.Hardware))
        {
            var sensors = hw.Sensors
                .Where(s => s.SensorType == SensorType.Power && s.Value is > 0 and < 5000)
                .ToList();
            if (sensors.Count == 0) continue;

            float watts = PickMain(hw, sensors);
            items.Add(new PowerItem(hw.Identifier.ToString(), Shorten(hw.Name), Kind(hw.HardwareType), watts));
        }
        return items;
    }

    private static float PickMain(IHardware hw, List<ISensor> sensors)
    {
        string[] preferred = hw.HardwareType switch
        {
            HardwareType.Cpu => ["Package", "CPU Package"],
            HardwareType.GpuNvidia => ["GPU Package", "GPU Board Power", "GPU Power"],
            HardwareType.GpuAmd => ["GPU Package", "GPU PPT", "GPU Total"],
            HardwareType.GpuIntel => ["GPU Package", "GPU Power"],
            HardwareType.Psu => ["Total Output", "Total", "Input"],
            HardwareType.Battery => ["Discharge Rate", "Charge Rate"],
            _ => [],
        };
        foreach (var p in preferred)
        {
            var s = sensors.FirstOrDefault(x => x.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s.Value!.Value;
        }
        var pkg = sensors.Where(x => x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                  || x.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)).ToList();
        return (pkg.Count > 0 ? pkg : sensors).Max(x => x.Value!.Value);
    }

    private static string Kind(HardwareType t) => t switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
        HardwareType.Memory => "RAM",
        HardwareType.Storage => "DISK",
        HardwareType.Psu => "PSU",
        HardwareType.Battery => "BAT",
        _ => "MISC",
    };

    private static string Shorten(string name) => name
        .Replace("NVIDIA GeForce ", "")
        .Replace("AMD Radeon(TM) ", "Radeon ")
        .Replace(" Processor", "")
        .Replace("(R)", "").Replace("(TM)", "")
        .Trim();

    public string Dump()
    {
        var sb = new StringBuilder();
        foreach (var hw in All(_pc.Hardware))
        {
            sb.AppendLine($"[{hw.HardwareType}] {hw.Name}  ({hw.Identifier})");
            foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Power))
                sb.AppendLine($"    {s.Name} = {s.Value}");
        }
        return sb.ToString();
    }

    public void Dispose() => _pc.Close();
}
