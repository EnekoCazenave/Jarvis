using System;
using System.Collections.Generic;
using System.IO;

namespace Jarvis.Tests
{
    internal static class ConfirmationTests
    {
        public static void Run()
        {
            AcceptsOnlyAfterPreview();
            RefusalAndStaleAgreementsHaveNoEffect();
            ConfirmationChannelsFollowApplicationRule();
            ConsumedAgreementCannotBeReplayed();
            ModelCannotForgeOrChangeAnAgreement();
            ChangedTargetNeedsNewAgreement();
            MissingEvidenceNeverInventsSuccessOrRetries();
        }

        private static void MissingEvidenceNeverInventsSuccessOrRetries()
        {
            using (var fixture = new Fixture())
            {
                var proofs = new[] { null,
                    new RenameEvidence(fixture.Source, fixture.Destination, false, "Non vérifié"),
                    new RenameEvidence(fixture.Destination, fixture.Destination, true, "Mauvaise source"),
                    new RenameEvidence(fixture.Source, fixture.Source, true, "Mauvaise destination"),
                    new RenameEvidence(fixture.Source, fixture.Destination, true, "") };
                fixture.Adapter.OverrideEvidence = true;
                foreach (var proof in proofs)
                {
                    fixture.Adapter.Evidence = proof;
                    int before = fixture.Adapter.Executed.Count;
                    var agreement = fixture.Prepare().Confirmation;
                    var result = fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen);
                    fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen);
                    TestRunner.Check(result.State == TaskState.Uncertain && result.RenameEvidence == null &&
                        fixture.Adapter.Executed.Count == before + 1, "No invented success or replay after absent or inconsistent evidence");
                }
                fixture.Adapter.Throw = true;
                int previous = fixture.Adapter.Executed.Count;
                var failedAgreement = fixture.Prepare().Confirmation;
                var failed = fixture.Orchestrator.Confirm(failedAgreement.Id, ConfirmationChannel.Screen);
                fixture.Orchestrator.Confirm(failedAgreement.Id, ConfirmationChannel.Screen);
                TestRunner.Check(failed.State == TaskState.Uncertain && fixture.Adapter.Executed.Count == previous + 1,
                    "An exception after launch consumes the agreement and never retries automatically");
            }
            Console.WriteLine("PASS missing/invalid evidence and adapter error stay uncertain without reusing agreement (orchestrator)");
        }

        private static void ChangedTargetNeedsNewAgreement()
        {
            using (var fixture = new Fixture())
            {
                var stale = fixture.Prepare().Confirmation;
                fixture.Adapter.Version = "version-2";
                var result = fixture.Orchestrator.Confirm(stale.Id, ConfirmationChannel.Screen);
                TestRunner.Check(result.State == TaskState.Failed && result.Confirmation == null && fixture.Adapter.Executed.Count == 0,
                    "A changed file invalidates the agreement before execution");
                fixture.Orchestrator.Confirm(stale.Id, ConfirmationChannel.Screen);
                TestRunner.Check(fixture.Adapter.Executed.Count == 0, "A stale target cannot be retried with the old agreement");
                var fresh = fixture.Prepare().Confirmation;
                TestRunner.Check(fresh.Id != stale.Id && fixture.Orchestrator.Confirm(fresh.Id, ConfirmationChannel.Screen).State == TaskState.Succeeded,
                    "A changed target can only execute after a new preview and agreement");
            }
            using (var fixture = new Fixture())
            {
                var stale = fixture.Prepare().Confirmation;
                File.Delete(fixture.Source);
                var result = fixture.Orchestrator.Confirm(stale.Id, ConfirmationChannel.Screen);
                TestRunner.Check(result.State == TaskState.Failed && fixture.Adapter.Executed.Count == 0, "A missing source never reaches execution");
            }
            Console.WriteLine("PASS changed or missing target invalidates agreement before execution (orchestrator)");
        }

        private static void ModelCannotForgeOrChangeAnAgreement()
        {
            using (var fixture = new Fixture())
            {
                File.WriteAllText(fixture.Destination, "Autre témoin");
                var proposals = new[] {
                    new ActionProposal("install_mcp", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", fixture.Destination } }),
                    new ActionProposal("open_document", new Dictionary<string, string> { { "path", fixture.Source } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", fixture.Destination }, { "approved", "true" } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", fixture.Destination }, { "requireScreen", "false" } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Destination }, { "destination", fixture.Source } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", fixture.Source } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", Path.Combine(fixture.Folder, "interdit.txt") } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", "relatif.txt" } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", fixture.Destination + ":secret" } }),
                    new ActionProposal("rename_witness", new Dictionary<string, string> { { "path", fixture.Source }, { "destination", @"\\server\partage\temoin.txt" } }),
                    new ActionProposal("rename_witness", null), null
                };
                foreach (var proposal in proposals)
                {
                    fixture.Reasoner.UseOverride = true;
                    fixture.Reasoner.Override = proposal;
                    var result = fixture.Prepare(true);
                    fixture.Orchestrator.Confirm(Guid.NewGuid(), ConfirmationChannel.SimulatedVoice);
                    TestRunner.Check(result.State == TaskState.Failed && result.Confirmation == null && fixture.Adapter.Executed.Count == 0,
                        "A model cannot authorize, substitute, or expand the requested witness operation");
                }
            }
            using (var fixture = new Fixture())
            {
                var agreement = fixture.Prepare().Confirmation;
                fixture.Reasoner.LastProposal.Parameters["path"] = fixture.Destination;
                fixture.Reasoner.LastProposal.Parameters["destination"] = fixture.Source;
                fixture.Reasoner.LastProposal.Parameters.Add("approved", "true");
                fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen);
                TestRunner.Check(agreement.Source == fixture.Source && agreement.Destination == fixture.Destination &&
                    fixture.Adapter.Executed.Count == 1 && fixture.Adapter.Executed[0].Source == fixture.Source &&
                    fixture.Adapter.Executed[0].Destination == fixture.Destination,
                    "Mutating the model proposal cannot alter the preview or the agreed action");
                TestRunner.Check(fixture.Reasoner.Input.Destination == fixture.Destination && fixture.Reasoner.Input.AvailableTools.Count == 1 &&
                    fixture.Reasoner.Input.AvailableTools[0] == "rename_witness(path, destination)", "Reasoning input describes only the requested witness operation");
            }
            Console.WriteLine("PASS forged/malformed model proposals refuse effects; prepared action is immutable (orchestrator)");
        }

        private static void ConsumedAgreementCannotBeReplayed()
        {
            using (var fixture = new Fixture())
            {
                var agreement = fixture.Prepare().Confirmation;
                fixture.Orchestrator.Changed += task => {
                    if (task.State == TaskState.Ready || task.State == TaskState.Executing || task.State == TaskState.Succeeded)
                    {
                        fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen);
                        fixture.Orchestrator.PrepareRename("Demande réentrante", fixture.Source, fixture.Destination);
                    }
                };
                var first = new System.Threading.Thread(() => fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen));
                var second = new System.Threading.Thread(() => fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.SimulatedVoice));
                first.Start(); second.Start(); first.Join(); second.Join();
                fixture.Orchestrator.Confirm(agreement.Id, ConfirmationChannel.Screen);
                TestRunner.Check(fixture.Adapter.Executed.Count == 1 && fixture.Orchestrator.Current.State == TaskState.Succeeded &&
                    fixture.Orchestrator.Current.RenameEvidence != null, "A04: concurrent, reentrant and replayed agreements execute only once");
            }
            Console.WriteLine("PASS consumed agreement cannot execute twice, including concurrent/reentrant confirmation (orchestrator)");
        }

        private static void ConfirmationChannelsFollowApplicationRule()
        {
            using (var fixture = new Fixture())
            {
                var screenOnly = fixture.Prepare(true).Confirmation;
                TestRunner.Check(screenOnly.Requirement == ConfirmationRequirement.ScreenOnly, "The preview shows the screen requirement");
                var rejected = fixture.Orchestrator.Confirm(screenOnly.Id, ConfirmationChannel.SimulatedVoice);
                TestRunner.Check(rejected.State == TaskState.AwaitingConfirmation && rejected.Confirmation == screenOnly && fixture.Adapter.Executed.Count == 0,
                    "A03: voice cannot satisfy a screen-only application rule");
                fixture.Orchestrator.Confirm(screenOnly.Id, (ConfirmationChannel)99);
                TestRunner.Check(fixture.Adapter.Executed.Count == 0, "Unknown confirmation channels cannot execute");
                TestRunner.Check(fixture.Orchestrator.Confirm(screenOnly.Id, ConfirmationChannel.Screen).State == TaskState.Succeeded,
                    "The active agreement can still be given on screen");
            }
            using (var fixture = new Fixture())
            {
                var limited = fixture.Prepare().Confirmation;
                TestRunner.Check(limited.Requirement == ConfirmationRequirement.VoiceOrScreen &&
                    fixture.Orchestrator.Confirm(limited.Id, ConfirmationChannel.SimulatedVoice).State == TaskState.Succeeded &&
                    fixture.Adapter.Executed.Count == 1, "A limited witness action accepts simulated voice when allowed by the application");
            }
            Console.WriteLine("PASS screen-only rule rejects voice; limited witness action accepts simulated voice (orchestrator)");
        }

        private static void RefusalAndStaleAgreementsHaveNoEffect()
        {
            using (var fixture = new Fixture())
            {
                var first = fixture.Prepare().Confirmation;
                TestRunner.Check(fixture.Orchestrator.Refuse(first.Id).State == TaskState.Refused, "User refusal is visible");
                fixture.Orchestrator.Confirm(first.Id, ConfirmationChannel.Screen);
                TestRunner.Check(fixture.Adapter.Executed.Count == 0, "Refused agreement cannot execute");

                var changed = fixture.Prepare().Confirmation;
                fixture.Orchestrator.InvalidateConfirmation();
                fixture.Orchestrator.Confirm(changed.Id, ConfirmationChannel.Screen);
                TestRunner.Check(fixture.Orchestrator.Current.State == TaskState.Cancelled && fixture.Adapter.Executed.Count == 0,
                    "Changing the request invalidates its agreement");

                var older = fixture.Prepare().Confirmation;
                var active = fixture.Prepare().Confirmation;
                fixture.Orchestrator.Confirm(older.Id, ConfirmationChannel.Screen);
                fixture.Orchestrator.Refuse(older.Id);
                TestRunner.Check(fixture.Orchestrator.Current.Confirmation == active && fixture.Adapter.Executed.Count == 0,
                    "A stale agreement cannot execute or refuse a new preparation, even for the same target");
                fixture.Orchestrator.Run("Autre demande", fixture.Source);
                fixture.Orchestrator.Confirm(active.Id, ConfirmationChannel.Screen);
                TestRunner.Check(fixture.Adapter.Executed.Count == 0, "A new opening request invalidates the prior rename agreement");
            }
            Console.WriteLine("PASS refusal, edited request, replaced preparation and new action invalidate agreements (orchestrator)");
        }

        private static void AcceptsOnlyAfterPreview()
        {
            using (var fixture = new Fixture())
            {
                TaskResult pending = fixture.Prepare();
                TestRunner.Check(pending.State == TaskState.AwaitingConfirmation && fixture.Adapter.Executed.Count == 0,
                    "Preparation must await agreement without changing a file");
                ConfirmationRequest confirmation = pending.Confirmation;
                TestRunner.Check(confirmation != null && confirmation.Source == fixture.Source &&
                    confirmation.Destination == fixture.Destination && !String.IsNullOrWhiteSpace(confirmation.Action) &&
                    !String.IsNullOrWhiteSpace(confirmation.Effects), "Preview identifies the exact action, target and effects");
                var result = fixture.Orchestrator.Confirm(confirmation.Id, ConfirmationChannel.Screen);
                TestRunner.Check(result.State == TaskState.Succeeded && fixture.Adapter.Executed.Count == 1 &&
                    fixture.Adapter.Executed[0].Source == fixture.Source && fixture.Adapter.Executed[0].Destination == fixture.Destination,
                    "Agreement executes the prepared rename once, then requires evidence");
            }
            Console.WriteLine("PASS confirmation preview -> screen agreement -> verified witness rename (orchestrator)");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "jarvis-confirmation-" + Guid.NewGuid());
            public readonly string Source, Destination;
            public readonly RenameAdapter Adapter = new RenameAdapter();
            public readonly Engine Reasoner = new Engine();
            public readonly Orchestrator Orchestrator;
            public Fixture()
            {
                Directory.CreateDirectory(Folder);
                Source = Path.Combine(Folder, "bonjour.txt");
                Destination = Path.Combine(Folder, "bonjour-renomme.txt");
                File.WriteAllText(Source, "Document témoin");
                Orchestrator = new Orchestrator(Reasoner, new DocumentPolicy(new[] { Source, Destination }), null, Adapter);
            }
            public TaskResult Prepare(bool requireScreen = false)
            { return Orchestrator.PrepareRename("Renomme le témoin", Source, Destination, requireScreen); }
            public void Dispose() { Directory.Delete(Folder, true); }
        }

        private sealed class Engine : IReasoner
        {
            public bool UseOverride;
            public ActionProposal Override;
            public ActionProposal LastProposal;
            public ReasoningInput Input;
            public ActionProposal Propose(ReasoningInput input)
            {
                Input = input;
                if (UseOverride) return Override;
                LastProposal = new ActionProposal("rename_witness", new Dictionary<string, string> {
                    { "path", input.Target }, { "destination", input.Destination }
                });
                return LastProposal;
            }
        }

        // The external filesystem boundary is replaced; the policy and orchestrator are real.
        private sealed class RenameAdapter : IWitnessRenameAdapter
        {
            public readonly List<RenameSnapshot> Executed = new List<RenameSnapshot>();
            public string Version = "version-1";
            public bool OverrideEvidence;
            public RenameEvidence Evidence;
            public bool Throw;
            public RenameSnapshot Prepare(string source, string destination)
            { return new RenameSnapshot(source, destination, Version); }
            public RenameEvidence Execute(RenameSnapshot snapshot)
            {
                Executed.Add(snapshot);
                if (Throw) throw new IOException("Erreur de renommage témoin");
                if (OverrideEvidence) return Evidence;
                return new RenameEvidence(snapshot.Source, snapshot.Destination, true, "Renommage témoin vérifié (simulation).");
            }
        }
    }
}
