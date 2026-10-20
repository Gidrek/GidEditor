using System.Collections.Generic;
using System.Threading.Tasks;

namespace GidEditor.Services;

/// <summary>Dialogs and OS integration the view models need from the window.</summary>
public interface IUiServices
{
    Task<string?> PickFolderAsync(string title);

    Task<IReadOnlyList<string>> PickImagesAsync();

    /// <summary>Asks for a line of text; returns null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialValue, string okText = "OK");

    /// <summary>Shows a message with the given buttons and returns the index of the one clicked, or -1 if dismissed.</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> buttons, int destructiveIndex = -1);

    void OpenExternal(string pathOrUrl);

    void RevealInFileManager(string path);
}
