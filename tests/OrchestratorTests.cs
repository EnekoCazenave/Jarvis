using System;
using System.Collections.Generic;
using System.IO;
using Jarvis;

namespace Jarvis.Tests
{
    internal static class TestRunner
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                AuthorizedDocumentOpensOnceAfterProof();
                Console.WriteLine("PASS authorized document opens once after proof (orchestrator)");
                RefusalsHaveNoEffect();
                UnverifiedOpeningNeverSucceeds();
                ReasoningContractReceivesRequestContextAndTools();
                if (Array.IndexOf(args, "--windows") >= 0) WindowsTests.Run();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void AuthorizedDocumentOpensOnceAfterProof()
        {
            string path = Path.Combine(Path.GetTempPath(), "jarvis-" + Guid.NewGuid() + ".txt");
            File.WriteAllText(path, "Document témoin");
            try
            {
                var adapter = new RecordingAdapter();
                var orchestrator = new Orchestrator(new SimulatedReasoner(SimulationScenario.Open), new DocumentPolicy(new[] { path }), adapter);
                var states = new List<TaskState>();
                orchestrator.Changed += task => states.Add(task.State);
                TaskResult result = orchestrator.Run("Ouvre le document témoin", path);
                Check(result.State == TaskState.Succeeded, "Success requires adapter evidence");
                Check(result.Target == path && result.Evidence != null, "The user sees the target and evidence");
                Check(adapter.Opened.Count == 1 && adapter.Opened[0] == path, "A01: one opening of the authorized target");
                Check(states.Contains(TaskState.Executing) && states[states.Count - 1] == TaskState.Succeeded, "Visible execution before success");
            }
            finally { File.Delete(path); }
        }

        internal static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void RefusalsHaveNoEffect()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jarvis-refusals-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "temoin.txt");
            string missing = Path.Combine(folder, "absent.txt");
            string other = Path.Combine(folder, "autre.txt");
            File.WriteAllText(target, "Témoin");
            File.WriteAllText(other, "Hors demande");
            try
            {
                var proposals = new[] {
                    new ActionProposal("run_command", new Dictionary<string, string> { { "path", target } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", target }, { "command", "extra" } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", Path.Combine(folder, "interdit.txt") } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", missing } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", other } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", target + ":secret" } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", @"\\server\share\document.txt" } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", "temoin.txt" } }),
                    new ActionProposal("open_document", null),
                    null
                };
                foreach (var proposal in proposals)
                {
                    var adapter = new RecordingAdapter();
                    var orchestrator = new Orchestrator(new FixedReasoner(proposal), new DocumentPolicy(new[] { target, missing, other }), adapter);
                    TaskResult result = orchestrator.Run("Ouvre mon témoin", target);
                    Check(result.State == TaskState.Failed && adapter.Opened.Count == 0, "A02: invalid proposals have no effect");
                    Check(result.Evidence == null, "No invented evidence on refusal");
                }
                var emptyAdapter = new RecordingAdapter();
                var empty = new Orchestrator(new SimulatedReasoner(SimulationScenario.Open), new DocumentPolicy(new[] { target }), emptyAdapter);
                Check(empty.Run(" ", target).State == TaskState.Failed && emptyAdapter.Opened.Count == 0, "Empty request has no effect");
                Console.WriteLine("PASS 10 forbidden/missing/malformed proposals and empty request: no effects (orchestrator)");
            }
            finally { Directory.Delete(folder, true); }
        }

        private static void UnverifiedOpeningNeverSucceeds()
        {
            string target = Path.Combine(Path.GetTempPath(), "jarvis-evidence-" + Guid.NewGuid() + ".txt");
            File.WriteAllText(target, "Témoin");
            try
            {
                foreach (DocumentEvidence evidence in new[] { null, new DocumentEvidence(target, false, "Non visible"), new DocumentEvidence(target + "other", true, "Wrong target"), new DocumentEvidence(target, true, "") })
                {
                    var adapter = new RecordingAdapter { OverrideEvidence = true, Evidence = evidence };
                    var orchestrator = new Orchestrator(new SimulatedReasoner(SimulationScenario.Open), new DocumentPolicy(new[] { target }), adapter);
                    TaskResult result = orchestrator.Run("Ouvre le témoin", target);
                    Check(result.State == TaskState.Uncertain && adapter.Opened.Count == 1, "No success and no retry without matching proof");
                    Check(orchestrator.Current == result, "Uncertain result retained");
                }
                var failedAdapter = new RecordingAdapter { Throw = true };
                var failed = new Orchestrator(new SimulatedReasoner(SimulationScenario.Open), new DocumentPolicy(new[] { target }), failedAdapter);
                Check(failed.Run("Ouvre", target).State == TaskState.Uncertain && failedAdapter.Opened.Count == 1, "Adapter error cannot invent success or retry");
                Console.WriteLine("PASS absent/false/mismatched/empty evidence and adapter exception: uncertain, no retry (orchestrator)");
            }
            finally { File.Delete(target); }
        }

        private static void ReasoningContractReceivesRequestContextAndTools()
        {
            string target = Path.Combine(Path.GetTempPath(), "jarvis-contract-" + Guid.NewGuid() + ".txt");
            File.WriteAllText(target, "Témoin");
            try
            {
                var engine = new FixedReasoner(new ActionProposal("open_document", new Dictionary<string, string> { { "path", target } }));
                var orchestrator = new Orchestrator(engine, new DocumentPolicy(new[] { target }), new RecordingAdapter());
                orchestrator.Run("Ma demande", target);
                Check(engine.Input.Request == "Ma demande" && engine.Input.Target == target, "Replaceable engine receives user input");
                Check(engine.Input.Context.Count > 0 && engine.Input.Context[0].Contains(target), "Context identifies its source");
                Check(engine.Input.AvailableTools.Count == 1 && engine.Input.AvailableTools[0] == "open_document(path)", "Only useful tool exposed");
                Console.WriteLine("PASS replacement reasoning engine receives request, context and available tools");
            }
            finally { File.Delete(target); }
        }

        private sealed class FixedReasoner : IReasoner
        {
            private readonly ActionProposal proposal;
            public ReasoningInput Input;
            public FixedReasoner(ActionProposal proposal) { this.proposal = proposal; }
            public ActionProposal Propose(ReasoningInput input) { Input = input; return proposal; }
        }

        private sealed class RecordingAdapter : IDocumentAdapter
        {
            public readonly List<string> Opened = new List<string>();
            public bool OverrideEvidence;
            public DocumentEvidence Evidence;
            public bool Throw;
            public DocumentEvidence Open(string path)
            {
                Opened.Add(path);
                if (Throw) throw new IOException("Erreur du lecteur de test");
                if (OverrideEvidence) return Evidence;
                return new DocumentEvidence(path, true, "Preuve simulée de l’adaptateur de test");
            }
        }
    }
}
