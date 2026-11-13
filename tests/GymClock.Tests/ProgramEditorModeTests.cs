using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// Loading a program into the editor (Apply to Editor, Import, Undo AI) must keep its mode.
    /// The radios used to be set one at a time, and the moment none was ticked read as "Shared",
    /// turning a Circuit program into Shared and dropping its BETWEEN move on save.
    /// </summary>
    public class ProgramEditorModeTests
    {
        private const string SharedProgram = "PROGRAM \"Default\"\nMODE SHARED\n\nWORK 30s\nREST 30s\n";

        private const string CircuitProgram = @"PROGRAM ""Buckers the ulitmate althlete""
MODE CIRCUIT

REPEAT 4
    WORK 45s
    REST 15s
END

BETWEEN
    MOVE 1m ""Rotate to the next station""
END BETWEEN

STATION ""Spin bike""
END STATION

STATION ""Box Step Ups""
END STATION

STATION ""Treadmill""
END STATION

STATION ""Skipping""
END STATION
";

        private static void RunSta(Action action)
        {
            Exception error = null;
            Thread t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Exception(error.Message, error);
        }

        [Theory]
        [InlineData(SharedProgram)]
        [InlineData(CircuitProgram)]
        public void ApplyingACircuitProgram_KeepsCircuitModeAndTheMove(string editorStartsWith)
        {
            RunSta(() =>
            {
                TimerSettings settings = new TimerSettings { ProgramScript = editorStartsWith };
                using (ProgramEditorForm editor = new ProgramEditorForm(settings))
                {
                    MethodInfo apply = typeof(ProgramEditorForm).GetMethod("ApplyProgram", BindingFlags.Instance | BindingFlags.NonPublic);
                    apply.Invoke(editor, new object[] { ProgramScriptFormat.Parse(CircuitProgram) });

                    WorkoutProgram program = (WorkoutProgram)typeof(ProgramEditorForm)
                        .GetField("_program", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);

                    Assert.Equal(ExecutionMode.Circuit, program.Mode);
                    string saved = ProgramScriptFormat.ToScript(program);
                    Assert.Contains("MODE CIRCUIT", saved);
                    Assert.Contains("MOVE 1m", saved);
                    Assert.Contains("REPEAT 4", saved);

                    // 4 stations x 4 x (45 + 15) + 3 moves of 1:00 = 19:00
                    int total = 0;
                    foreach (var b in program.BuildRuntimeSequence()) total += b.Block.Seconds;
                    Assert.Equal(19 * 60, total);
                }
            });
        }

        [Fact]
        public void ApplyingASharedProgram_OverACircuitEditor_IsShared()
        {
            RunSta(() =>
            {
                using (ProgramEditorForm editor = new ProgramEditorForm(new TimerSettings { ProgramScript = CircuitProgram }))
                {
                    typeof(ProgramEditorForm).GetMethod("ApplyProgram", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, new object[] { ProgramScriptFormat.Parse(SharedProgram) });
                    WorkoutProgram program = (WorkoutProgram)typeof(ProgramEditorForm)
                        .GetField("_program", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                    Assert.Equal(ExecutionMode.Shared, program.Mode);
                }
            });
        }
    }
}
