namespace Sfp.Cli;

/// <summary>Process exit codes from the PRD command line section.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ValidationError = 2;
    public const int OneNoteUnavailable = 3;
    public const int RenderError = 4;
    public const int WriteError = 5;
    public const int RefreshSourceUnreachable = 6;
}
