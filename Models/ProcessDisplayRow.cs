using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using VramMonitor.Services;

namespace VramMonitor.Models;

public sealed class ProcessDisplayRow : INotifyPropertyChanged
{
    public override string ToString() => $"{Name} (PID {PidText})";
    public event PropertyChangedEventHandler? PropertyChanged;
    public uint Pid { get; init; }
    public bool IsSystem { get; init; }
    public string Key => IsSystem ? "SYSTEM" : Pid.ToString();
    public string PidText => IsSystem ? "—" : Pid.ToString();
    public double RowOpacity => IsSystem ? 0.65 : 1;
    public string Name { get; private set; } = "";
    public string? ExePath { get; private set; }
    public DateTime? StartTimeUtc { get; private set; }
    public ulong LocalBytes { get; private set; }
    public ulong NonLocalBytes { get; private set; }
    public string DedicatedText => FormatHelper.FormatVram(LocalBytes);
    public string SharedText => FormatHelper.FormatVram(NonLocalBytes);
    public ImageSource? Icon { get; private set; }

    public void Update(string name, string? path, DateTime? startTimeUtc, ulong local, ulong shared)
    {
        bool identityChanged = ExePath != path || StartTimeUtc != startTimeUtc;
        Name = name; ExePath = path; StartTimeUtc = startTimeUtc; LocalBytes = local; NonLocalBytes = shared;
        if (identityChanged) Icon = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetIcon(ImageSource icon)
    {
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }
}
