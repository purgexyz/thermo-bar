using LibreHardwareMonitor.Hardware;

namespace TempBar;

public readonly record struct Reading(float? Cpu, float? Gpu, string? CpuName, string? GpuName);

/// <summary>
/// Opens only the CPU and GPU groups and updates only the two chosen devices per tick.
/// </summary>
public sealed class SensorReader : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    private IHardware? _cpu;
    private IHardware? _gpu;
    private ISensor? _cpuSensor;
    private ISensor? _gpuSensor;

    public void Open()
    {
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpu = PickGpu(_computer.Hardware);
    }

    public Reading Read()
    {
        if (_cpu != null)
        {
            _cpu.Update();
            _cpuSensor ??= PickCpuSensor(_cpu);
        }
        if (_gpu != null)
        {
            _gpu.Update();
            _gpuSensor ??= PickGpuSensor(_gpu);
        }

        return new Reading(Valid(_cpuSensor?.Value), Valid(_gpuSensor?.Value), _cpu?.Name, _gpu?.Name);
    }

    private static float? Valid(float? v) => v is > 0 and < 150 ? v : null;

    private static IHardware? PickGpu(IEnumerable<IHardware> hw)
    {
        var gpus = hw.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();
        return gpus.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
            ?? gpus.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
            ?? gpus.FirstOrDefault();
    }

    private static ISensor? PickCpuSensor(IHardware cpu)
    {
        var temps = AllSensors(cpu).Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        if (temps.Count == 0) return null;

        string[] preferred = ["CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "Tctl", "Package"];
        foreach (var name in preferred)
        {
            var match = temps.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return temps
            .Where(s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(s => s.Name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            ?? temps.FirstOrDefault(s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase));
    }

    private static ISensor? PickGpuSensor(IHardware gpu)
    {
        var temps = AllSensors(gpu).Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        return temps.FirstOrDefault(s => s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            ?? temps.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            ?? temps.FirstOrDefault();
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hw) =>
        hw.Sensors.Concat(hw.SubHardware.SelectMany(AllSensors));

    public void Dispose() => _computer.Close();
}
