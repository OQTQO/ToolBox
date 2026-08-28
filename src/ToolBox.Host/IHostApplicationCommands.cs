namespace ToolBox.Host;

internal interface IHostApplicationCommands
{
    void HideMainWindowToTray();

    void RequestShutdown();

    void RequestRestart();
}
