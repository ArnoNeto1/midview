namespace Midview.Protocol;

// How "Fechar programas" ends the programs (CloseAppsCommand).
public enum CloseAppsMode : byte
{
    // Like clicking each window's X: programs with unsaved work ask first.
    Ask = 0,

    // End the programs without asking (the warning before it was the
    // student's time to save) -- unsaved work is lost.
    Force = 1,
}
