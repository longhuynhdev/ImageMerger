using Microsoft.UI.Xaml;

namespace ImageMerger.Helpers;

/// <summary>
/// Hàm tĩnh dùng trong x:Bind thay cho IValueConverter, ví dụ:
/// <c>Visibility="{x:Bind helpers:XamlFunctions.ToVisibility(ViewModel.IsEmpty), Mode=OneWay}"</c>.
/// </summary>
public static class XamlFunctions
{
    public static Visibility ToVisibility(bool isVisible) =>
        isVisible ? Visibility.Visible : Visibility.Collapsed;
}
