# Jarvis

Tranches des tickets [#2](https://github.com/EnekoCazenave/Jarvis/issues/2) et [#3](https://github.com/EnekoCazenave/Jarvis/issues/3) : ouvrir un document témoin dans Windows, ou préparer son renommage réversible et l’accepter ou le refuser après consultation de ses effets. Le raisonnement reste simulé ; les effets sur les témoins sont réels et vérifiés.

## Lancer sous Windows

Depuis la racine du dépôt, dans PowerShell :

```powershell
.\build.ps1
.\build\Jarvis.exe
```

Prérequis : Windows 10/11 x64 avec .NET Framework 4.x et son compilateur système (`Microsoft.NET\Framework64\v4.0.30319\csc.exe`). Aucun SDK .NET, paquet NuGet, serveur llama.cpp ou modèle à télécharger. Si la politique PowerShell bloque les scripts locaux, une exécution ponctuelle est possible avec `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.

La fenêtre indique **MODE SIMULÉ**. Saisir une demande non vide, choisir l’opération et le scénario puis lancer la démonstration. Le scénario détermine la proposition ; le texte n’est pas interprété par un modèle. Les scénarios permettent une proposition conforme, une cible absente ou interdite, un outil interdit, ou un accord fabriqué par le moteur (refusé). La cible, l’état et le résultat sont visibles.

Le document `witnesses/bonjour.txt` est copié dans `build/witnesses` lors de la compilation. Le lecteur Jarvis affiche ce document en lecture seule dans une seconde fenêtre Windows. Fermer Jarvis ferme ses lecteurs.

### Confirmer un renommage témoin

Choisir le renommage et son sens : `bonjour.txt` vers `bonjour-renomme.txt`, ou l’inverse. La préparation affiche l’action, le chemin source, le chemin destination et les effets. Aucun fichier ne change avant l’accord. **Refuser** termine la demande sans effet. Un accord à l’écran exécute le renommage, puis Jarvis vérifie que le même fichier porte le nouveau nom et que son contenu est conservé.

Le bouton d’accord vocal est explicitement **simulé**. La case imposant l’accord à l’écran est une règle de l’application : si elle est cochée avant la préparation, un clic vocal laisse l’action en attente. Le moteur ne peut pas changer cette règle ni fournir un accord. Aucun microphone, reconnaissance vocale ou mécanisme d’authentification n’est utilisé.

Modifier la demande, l’opération, le sens, le scénario ou la règle invalide la confirmation en attente. Préparer une autre action remplace aussi l’ancien accord. Si le fichier change, disparaît ou si la destination apparaît pendant l’attente, Jarvis refuse de réutiliser l’accord. Le retour au nom initial exige de choisir le sens inverse puis de donner un nouvel accord. La compilation conserve les témoins déjà présents, y compris le nom obtenu après renommage.

## Pile et frontières

- **C# / .NET Framework / Windows Forms** : pile Windows minimale, compilable sur le PC cible sans dépendance téléchargée. Le script compile avec les avertissements traités en erreurs, ce qui vérifie aussi les types. Le langage reste compatible avec le compilateur fourni par Windows ; une évolution vers un SDK moderne reste possible ultérieurement.
- **`IReasoner.Propose(ReasoningInput)`** : reçoit demande, cible sélectionnée, contexte sourcé et outils disponibles ; renvoie une proposition structurée. Le moteur simulé est remplaçable par une autre implémentation de ce contrat. Il ne reçoit ni adaptateur ni objet de permissions.
- **`Orchestrator.Run` et `Changed`** : frontière publique des tests, préparation puis contrôle de la proposition, exécution unique, vérification du résultat et conservation du dernier état. Aucun nouvel essai automatique. Une preuve absente, incohérente ou une exception après le début d’exécution produit un résultat incertain.
- **`Orchestrator.PrepareRename`, `Confirm`, `Refuse` et `InvalidateConfirmation`** : la préparation produit une confirmation immuable liée à l’action, aux deux chemins et à la version du fichier. `Confirm` reçoit seulement l’identifiant et le canal, jamais de nouveaux paramètres d’action. L’accord est consommé avant les événements et l’adaptateur ; un verrou et une garde d’exécution empêchent les doubles clics, appels simultanés et appels réentrants de le réutiliser. Les confirmations restent en mémoire pour la session, sans reprise automatique après fermeture.
- **`DocumentPolicy`** : liste fermée de témoins `.txt`, chemin local absolu et paramètres exacts. Seul l’outil correspondant à l’opération choisie est accepté. Une proposition visant une autre source ou destination que la sélection ne peut pas être exécutée. Le chemin `absent.txt` est réservé au scénario d’absence ; aucun fichier n’est créé pour ce scénario.
- **`WindowsDocumentAdapter`** : recontrôle l’autorisation, ouvre le fichier sans partage d’écriture/suppression, vérifie le chemin final du handle Windows et affiche le texte. Il vérifie la visibilité native du lecteur et l’égalité entre texte lu et texte affiché. L’empreinte SHA-256 identifie le texte affiché, pas une affirmation du moteur.
- **`WindowsWitnessRenameAdapter`** : autorise seulement l’aller-retour entre les deux noms témoins d’un même dossier local. Il compare l’identité Windows, les métadonnées et le SHA-256 avant exécution. Il verrouille les ancêtres et le fichier, refuse liens et jonctions, puis renomme le même handle Windows sans écrasement. La preuve vérifie le nouveau chemin du handle, l’absence de l’ancien nom et la conservation de l’identité et des octets. Un accès ou un verrou impossible produit un refus, pas une protection annoncée à tort.

Le lecteur dédié donne une preuve que le contenu est affiché. Ce ticket n’utilise pas l’application associée par défaut aux `.txt` : le succès de `Process.Start` ne suffirait pas à prouver l’ouverture dans une application tierce. Les associations Windows et les autres formats ne sont pas validés par ce parcours.

Le lecteur accepte les témoins locaux de 128 Kio maximum, UTF-8 strict ou encodage Unicode avec BOM, sans octet nul. Le renommage est limité aux témoins UTF-8 de 128 Kio maximum, sans caractère nul, modifiables et sans liens. Une destination occupée est toujours refusée. Les opérations de fichiers générales, l’historique d’annulation, les installations MCP et les permissions supplémentaires restent dans les tickets suivants. La politique de témoins ne constitue pas une isolation de processus MCP ; le code s’exécute avec les droits du compte Windows, sans promesse de sandbox.

## Vérification

```powershell
.\build.ps1 -Test          # tests déterministes à la frontière de l’orchestrateur
.\build.ps1 -ConfirmationTest # tests ciblés du ticket #3
.\build.ps1 -WindowsTest   # suite complète + vraies fenêtres et vrais fichiers temporaires
```

Les tests utilisent un exécutable C# autonome, sans framework de test externe. Toute assertion échouée renvoie un code non nul. Les tests déterministes remplacent uniquement le moteur et l’adaptateur Windows. Ils couvrent A01/A02 : une ouverture autorisée, absence d’effets pour les propositions interdites ou mal formées, et absence de réussite sans preuve valide. Ils vérifient aussi le contrat du moteur remplaçable.

Les tests de confirmation couvrent l’aperçu sans effet, accord et refus, changement de demande ou de cible, accords périmés et consommés, confirmations simultanées et réentrantes, canaux autorisés et moteur hostile. Une mutation de sa proposition après préparation ne peut pas modifier l’action confirmée. Les résultats absents ou incohérents et exceptions après lancement restent incertains, sans répétition de l’accord.

L’option `-WindowsTest` exige une session de bureau Windows interactive. Elle ouvre puis ferme uniquement les fenêtres du test et utilise des fichiers temporaires réels. Elle vérifie le lecteur, le renommage aller-retour, le contenu changé à taille et date identiques, le remplacement du fichier, une destination occupée et une jonction Windows. Elle exerce aussi les boutons de confirmation, le refus oral d’une règle écran et l’invalidation depuis l’interface. Les captures `build/windows-ui.png` et `build/confirmation-ui.png` permettent l’inspection visuelle. Les fichiers temporaires sont supprimés à la fin.

La suite complète, dont les tests Windows de lecture et confirmation, a été exécutée avec succès sur le PC de développement le 8 septembre 2026, avec contrôle des types et avertissements en erreurs. Les deux captures ont été inspectées. Cela valide le lecteur intégré et le renommage témoin ; aucun essai de voix réelle, MCP externe, application associée, llama.cpp ou qualité de compréhension n’est revendiqué.
