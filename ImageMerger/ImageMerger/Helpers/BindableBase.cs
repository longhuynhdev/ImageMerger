using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageMerger.Helpers;

/// <summary>
/// Lớp cơ sở cài đặt <see cref="INotifyPropertyChanged"/> bằng tay.
///
/// INotifyPropertyChanged chỉ có một thành viên: sự kiện <see cref="PropertyChanged"/>.
/// x:Bind với <c>Mode=OneWay</c>/<c>TwoWay</c> đăng ký vào sự kiện này; mỗi khi ta raise nó
/// kèm tên property, binding đọc lại giá trị mới và cập nhật control.
/// Không raise thì UI không bao giờ biết giá trị đã đổi.
/// </summary>
public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gán <paramref name="value"/> vào <paramref name="field"/> và báo cho UI, nhưng chỉ khi giá trị thật sự đổi
    /// (tránh vòng lặp vô hạn với binding TwoWay và tránh vẽ lại thừa).
    /// </summary>
    /// <param name="propertyName">
    /// [CallerMemberName]: compiler tự điền tên property đang gọi, nên trong setter chỉ cần
    /// <c>SetProperty(ref _name, value)</c>, không phải gõ chuỗi "Name".
    /// </param>
    /// <returns><c>true</c> nếu giá trị đã đổi, để setter biết mà báo thêm cho các property phụ thuộc.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Báo cho UI rằng <paramref name="propertyName"/> đã đổi. Dùng cho property tính toán (chỉ có get).</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Báo nhiều property một lúc, tiện cho các property phụ thuộc vào cùng một giá trị.</summary>
    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (string name in propertyNames) OnPropertyChanged(name);
    }
}
