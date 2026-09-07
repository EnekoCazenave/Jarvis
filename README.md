# Jarvis

Première tranche du [ticket #2](https://github.com/EnekoCazenave/Jarvis/issues/2) : saisir une demande de démonstration, proposer une action avec un moteur simulé et ouvrir un document témoin dans une fenêtre Windows dont le contenu est vérifié.

## Lancer sous Windows

Depuis la racine du dépôt, dans PowerShell :

```powershell
.\build.ps1
.\build\Jarvis.exe
```

Prérequis : Windows 10/11 x64 avec .NET Framework 4.x et son compilateur système (`Microsoft.NET\Framework64\v4.0.30319\csc.exe`). Aucun SDK .NET, paquet NuGet, serveur llama.cpp ou modèle à télécharger. Si la politique PowerShell bloque les scripts locaux, une exécution ponctuelle est possible avec `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.

La fenêtre indique **MODE SIMULÉ**. Saisir une demande non vide, choisir un scénario puis cliquer sur **Exécuter la démonstration**. Le scénario détermine la proposition ; le texte n’est pas interprété par un modèle. Les quatre scénarios sont ouverture autorisée, cible absente, cible non autorisée et outil interdit. La cible proposée, l’état et le résultat sont visibles.

Le document `witnesses/bonjour.txt` est copié dans `build/witnesses` lors de la compilation. Le lecteur Jarvis affiche ce document en lecture seule dans une seconde fenêtre Windows. Fermer Jarvis ferme ses lecteurs.

## Pile et frontières

- **C# / .NET Framework / Windows Forms** : pile Windows minimale, compilable sur le PC cible sans dépendance téléchargée. Le script compile avec les avertissements traités en erreurs, ce qui vérifie aussi les types. Le langage reste compatible avec le compilateur fourni par Windows ; une évolution vers un SDK moderne reste possible ultérieurement.
- **`IReasoner.Propose(ReasoningInput)`** : reçoit demande, cible sélectionnée, contexte sourcé et outils disponibles ; renvoie une proposition structurée. Le moteur simulé est remplaçable par une autre implémentation de ce contrat. Il ne reçoit ni adaptateur ni objet de permissions.
- **`Orchestrator.Run` et `Changed`** : frontière publique des tests, préparation puis contrôle de la proposition, exécution unique, vérification du résultat et conservation du dernier état. Aucun nouvel essai automatique. Une preuve absente, incohérente ou une exception après le début d’exécution produit un résultat incertain.
- **`DocumentPolicy`** : liste fermée de témoins `.txt`, chemin local absolu, outil unique et paramètres exacts. Une proposition visant un autre document que la sélection ne peut pas être exécutée. Le chemin `absent.txt` est réservé au scénario d’absence ; aucun fichier n’est créé pour ce scénario.
- **`WindowsDocumentAdapter`** : recontrôle l’autorisation, ouvre le fichier sans partage d’écriture/suppression, vérifie le chemin final du handle Windows et affiche le texte. Il vérifie la visibilité native du lecteur et l’égalité entre texte lu et texte affiché. L’empreinte SHA-256 identifie le texte affiché, pas une affirmation du moteur.

Le lecteur dédié donne une preuve que le contenu est affiché. Ce ticket n’utilise pas l’application associée par défaut aux `.txt` : le succès de `Process.Start` ne suffirait pas à prouver l’ouverture dans une application tierce. Les associations Windows et les autres formats ne sont pas validés par ce parcours.

Les limites fixes de ce premier adaptateur sont les témoins locaux, les textes de 128 Kio maximum, UTF-8 strict ou encodage Unicode avec BOM, sans octet nul. Le choix de scénario est le seul réglage de moteur introduit et il est utilisable dans l’application. Aucun réglage de modèle réel, voix ou mémoire n’est ajouté ici. La politique de témoins ne constitue pas une isolation de processus MCP ; le code s’exécute avec les droits du compte Windows, sans promesse de sandbox.

## Vérification

```powershell
.\build.ps1 -Test          # tests déterministes à la frontière de l’orchestrateur
.\build.ps1 -WindowsTest   # suite complète + vraies fenêtres et vrais fichiers temporaires
```

Les tests utilisent un exécutable C# autonome, sans framework de test externe. Toute assertion échouée renvoie un code non nul. Les tests déterministes remplacent uniquement le moteur et l’adaptateur Windows. Ils couvrent A01/A02 : une ouverture autorisée, absence d’effets pour les propositions interdites ou mal formées, et absence de réussite sans preuve valide. Ils vérifient aussi le contrat du moteur remplaçable.

L’option `-WindowsTest` exige une session de bureau Windows interactive. Elle ouvre puis ferme uniquement les fenêtres du test, lit un fichier temporaire réel, vérifie l’effet de l’adaptateur, puis exerce la saisie et le bouton de l’application avec les quatre scénarios. Une capture de la fenêtre est produite dans `build/windows-ui.png` pour inspection visuelle. Les fichiers temporaires sont supprimés à la fin.

Les tests Windows ont été exécutés avec succès sur le PC de développement le 7 septembre 2026, ainsi que la compilation avec contrôle des types et avertissements en erreurs. Cela valide le lecteur Windows intégré ; aucun essai de voix, MCP externe, application associée, llama.cpp ou qualité de compréhension n’est revendiqué.
