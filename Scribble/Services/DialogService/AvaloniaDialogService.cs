using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Scribble.Services.DialogService;

public class AvaloniaDialogService : IDialogService
{
    public async Task<bool> ShowWarningConfirmationAsync(string title, string message)
    {
        var box = MessageBoxManager
            .GetMessageBoxStandard(title,
                message,
                ButtonEnum.YesNo,
                Icon.Warning);

        var result = await box.ShowAsync();
        return result == ButtonResult.Yes;
    }
}