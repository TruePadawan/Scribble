using System.Threading.Tasks;

namespace Scribble.Services.DialogService;

public interface IDialogService
{
    Task<bool> ShowWarningConfirmationAsync(string title, string message);
}