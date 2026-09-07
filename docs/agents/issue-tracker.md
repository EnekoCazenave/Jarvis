# Issue tracker: GitHub

Specs et tickets vivent dans GitHub Issues, dépôt `EnekoCazenave/Jarvis`. Utiliser la CLI `gh` ; préciser `--repo EnekoCazenave/Jarvis` lorsque la commande n’est pas exécutée dans le clone de ce dépôt.

## Opérations

- Créer : `gh issue create --repo EnekoCazenave/Jarvis --title "Titre" --body-file corps.md`.
- Lire : `gh issue view NUMERO --repo EnekoCazenave/Jarvis --comments`. Consulter aussi les étiquettes.
- Lister : `gh issue list --repo EnekoCazenave/Jarvis --state open --json number,title,body,labels`, avec les filtres adaptés.
- Commenter : `gh issue comment NUMERO --repo EnekoCazenave/Jarvis --body-file commentaire.md`.
- Étiqueter : `gh issue edit NUMERO --repo EnekoCazenave/Jarvis --add-label ETIQUETTE` ou `--remove-label ETIQUETTE`.
- Fermer : `gh issue close NUMERO --repo EnekoCazenave/Jarvis` ; consigner d’abord le résultat pertinent si nécessaire.

Pour les contenus multilignes, écrire le texte exact dans un fichier temporaire et utiliser `--body-file` afin de préserver les retours à la ligne et éviter l’interprétation par le shell.

« Publier au système de suivi » signifie créer une issue GitHub. « Récupérer le ticket » signifie lire l’issue et ses commentaires. Rechercher une issue existante correspondant à la spec avant d’en créer une autre.

## Pull requests as a triage surface

**PRs as a request surface: no.**

Les issues et pull requests partagent une numérotation ; résoudre la nature d’une référence ambiguë avant d’agir.

## Wayfinding

- Carte : une issue avec l’étiquette `wayfinder:map`, contenant notes, décisions et incertitudes.
- Enfants : une issue par ticket, reliée à la carte par sous-issue GitHub ; à défaut, utiliser une liste de tâches dans la carte et une référence `Part of #NUMERO` dans l’enfant.
- Types : `wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, `wayfinder:task`, à utiliser lorsque ce workflow est invoqué.
- Dépendances : utiliser les dépendances natives GitHub ; à défaut, une ligne `Blocked by: #NUMERO` et vérifier que tous les bloqueurs sont fermés.
- Frontière : enfants ouverts, sans bloqueur ouvert et non assignés, dans l’ordre de la carte.
- Prise en charge : assigner l’issue avant de commencer le travail.
- Résolution : ajouter le résultat, fermer l’issue et ajouter à la carte une synthèse avec le lien vers l’enfant.
