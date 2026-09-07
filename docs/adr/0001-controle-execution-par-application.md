---
status: accepted
---

# L’application contrôle les permissions et l’exécution

L’assistant doit utiliser un petit modèle local et découvrir de nouvelles capacités MCP, tout en accédant aux fichiers du PC. Nous confions les autorisations, les confirmations et la vérification des résultats à l’application : le modèle propose des actions mais ne peut pas modifier ses droits. Cette séparation ajoute un orchestrateur et des contraintes d’isolation aux adaptateurs, en échange de règles qui ne reposent pas sur l’obéissance du modèle à un prompt ; la vérification des demandes ne remplace pas l’isolation effective des processus MCP.
