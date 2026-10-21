using System.Xml.Linq;
using Sfp.Refresh.Scheduler;
using Sfp.Refresh.Tests.Support;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Scheduler;

/// <summary>
/// refresh-16: a task command longer than schtasks' 261-character /TR limit is registered from a task definition file
/// (/XML) holding the same trigger, principal and settings, with the program and its arguments apart.
/// </summary>
public class ScheduledTaskXmlTests
{
    private const string Exe = @"C:\Tools\sfp2onenote.exe";
    private const string Name = "SFP Q1 2027";
    private static readonly XNamespace T = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>A notebooks folder long enough to push the /TR value past 261 characters.</summary>
    private static readonly string LongFolder = @"C:\Users\someone\OneDrive - " + new string('x', 150) + @"\Documents\OneNote Notebooks";

    [Fact]
    public void A_short_command_still_uses_TR()
    {
        DefinitionReadingRunner runner = new();
        string folder = @"C:\Users\me\Documents\OneNote Notebooks";

        new ScheduledTaskService(runner, new CapturingLog().Logger, true, TestPaths.NewTempDir("schtasks")).Register(Name, Exe, 14, folder);

        runner.Arguments.Should().Equal(ScheduledTaskService.CreateArguments(Name, Exe, 14, folder));
        runner.Arguments[8].Should().Be("\"C:\\Tools\\sfp2onenote.exe\" refresh --notebook \"SFP Q1 2027\" --calendar --horizon 14 --folder \"C:\\Users\\me\\Documents\\OneNote Notebooks\"");
    }

    [Fact]
    public void A_long_folder_registers_the_task_from_a_definition_file_that_is_deleted_afterwards()
    {
        ScheduledTaskService.TaskRun(Name, Exe, 14, LongFolder).Length.Should().BeGreaterThan(ScheduledTaskService.MaxTaskRunLength);
        DefinitionReadingRunner runner = new();
        string temp = TestPaths.NewTempDir("schtasks");

        new ScheduledTaskService(runner, new CapturingLog().Logger, true, temp).Register(Name, Exe, 14, LongFolder);

        runner.Arguments.Should().HaveCount(6);
        runner.Arguments.Take(4).Should().Equal("/Create", "/TN", "sfp2onenote refresh SFP Q1 2027", "/XML");
        runner.Arguments[5].Should().Be("/F");
        Path.GetDirectoryName(runner.Arguments[4]).Should().Be(temp.TrimEnd(Path.DirectorySeparatorChar));
        File.Exists(runner.Arguments[4]).Should().BeFalse("the definition file is deleted once schtasks has read it");
        runner.Bytes.Take(2).Should().Equal(new byte[] { 0xFF, 0xFE }, "the file is UTF-16, as its declaration says");

        XElement task = XDocument.Parse(runner.Definition!).Root!;
        XElement exec = task.Element(T + "Actions")!.Element(T + "Exec")!;
        exec.Element(T + "Command")!.Value.Should().Be(Exe);
        exec.Element(T + "Arguments")!.Value.Should().Be("refresh --notebook \"SFP Q1 2027\" --calendar --horizon 14 --folder \"" + LongFolder + "\"");
        XElement trigger = task.Element(T + "Triggers")!.Element(T + "CalendarTrigger")!;
        trigger.Element(T + "StartBoundary")!.Value.Should().EndWith("T05:00:00");
        trigger.Element(T + "ScheduleByDay")!.Element(T + "DaysInterval")!.Value.Should().Be("1");
        XElement principal = task.Element(T + "Principals")!.Element(T + "Principal")!;
        principal.Element(T + "LogonType")!.Value.Should().Be("InteractiveToken", "like /TR without /RU, it runs only while the user is logged on");
        principal.Element(T + "RunLevel")!.Value.Should().Be("LeastPrivilege");
        task.Element(T + "RegistrationInfo")!.Element(T + "Description")!.Value.Should().Contain("SFP Q1 2027");
    }

    [Fact]
    public void Special_characters_in_the_notebook_name_survive_the_definition()
    {
        string definition = ScheduledTaskService.TaskXml("R&D <Q1> 2027", Exe, 7, LongFolder, new DateTime(2027, 1, 4));

        XElement task = XDocument.Parse(definition).Root!;
        task.Element(T + "Actions")!.Element(T + "Exec")!.Element(T + "Arguments")!.Value
            .Should().Be("refresh --notebook \"R&D <Q1> 2027\" --calendar --horizon 7 --folder \"" + LongFolder + "\"");
        task.Element(T + "Triggers")!.Element(T + "CalendarTrigger")!.Element(T + "StartBoundary")!.Value.Should().Be("2027-01-04T05:00:00");
    }

    [Fact]
    public void A_failed_create_from_a_definition_is_reported_and_the_file_is_still_deleted()
    {
        DefinitionReadingRunner runner = new() { Result = new ProcessResult(1, "", "ERROR: The task XML is malformed.") };

        Action register = () => new ScheduledTaskService(runner, new CapturingLog().Logger, true, TestPaths.NewTempDir("schtasks")).Register(Name, Exe, 14, LongFolder);

        register.Should().Throw<InvalidOperationException>().WithMessage("*register*exit code 1*task XML is malformed*");
        File.Exists(runner.Arguments[4]).Should().BeFalse();
    }

    /// <summary>Records the schtasks arguments and, for /XML, reads the definition file while it exists.</summary>
    private sealed class DefinitionReadingRunner : IProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

        public string? Definition { get; private set; }

        public byte[] Bytes { get; private set; } = Array.Empty<byte>();

        public ProcessResult Result { get; init; } = new(0, "SUCCESS", "");

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Arguments = arguments.ToList();
            int xml = Arguments.ToList().IndexOf("/XML");
            if (xml >= 0)
            {
                Bytes = File.ReadAllBytes(Arguments[xml + 1]);
                Definition = File.ReadAllText(Arguments[xml + 1]);
            }

            return Result;
        }
    }
}
