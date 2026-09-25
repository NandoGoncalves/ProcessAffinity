# ProcessAffinity

Application WPF de gestion des processus Windows — un Gestionnaire des tâches
étendu. Écrite en 2010 sous Visual Studio 2010 (.NET Framework 4.6.1, x86),
en cours de modernisation.

## Objectif

Afficher l'utilisation CPU par processus et permettre de modifier l'affinité
processeur et la priorité sur un ou plusieurs processus sélectionnés.

La valeur de l'outil tient à ce que le Gestionnaire des tâches de Windows 11
n'expose pas ou mal : l'affinité, les CPU Sets, le mode d'efficacité (EcoQoS)
et la distinction entre P-cores et E-cores.

## Décisions d'architecture

- **Cible** : `net9.0-windows`, `PlatformTarget` **x64 uniquement**. Pas
  d'AnyCPU : on ne veut jamais tourner sous WOW64 (masque d'affinité tronqué
  à 32 bits, `MainModule.FileName` inopérant sur les cibles 64 bits).
- `UseWPF` et `UseWindowsForms` à true (le second pour le `NotifyIcon`).
- **Local uniquement.** Le pilotage de machines distantes est abandonné pour
  l'instant. Il pourra revenir plus tard derrière une interface
  `IProcessProvider` exposant ses capacités, l'affinité n'étant pas réalisable
  à distance via WMI. Supprimer à l'étape 2 la saisie d'identifiants, les
  `ConnectionOptions` et l'usage de `CSName`.
- **Droits** : ceux de l'utilisateur qui lance l'application. Pas de
  `requireAdministrator` dans le manifeste — à charge de l'utilisateur de
  faire un « Exécuter en tant qu'administrateur ». L'application détecte son
  niveau d'élévation, l'affiche dans l'IHM, et propose une relance élevée
  (`ProcessStartInfo` avec `Verb = "runas"`).
- **Objectif à terme** : éliminer WMI et `System.Management` au profit de
  `System.Diagnostics.Process` et de P/Invoke.

## Plan de migration

**Étape 1 — baseline compilable.** Passage en .NET 9 / x64 sans aucun
changement fonctionnel. WMI et `Win32_Process.cs` sont conservés en l'état.
Marquée par le tag git `baseline-net9`.

**Étape 2 — remplacement de la couche de collecte.**
- `%` CPU calculé par delta de `Process.TotalProcessorTime` sur le temps
  écoulé, rapporté au nombre de cœurs. Plus aucune requête
  `Win32_PerfFormattedData_PerfProc_Process`.
- Détection création/suppression par diff périodique sur
  `Process.GetProcesses()` (1 à 2 s), ou ETW via `TraceEvent` si le temps réel
  s'avère nécessaire. Abandon du watcher `__InstanceOperationEvent`.
- Suppression de `Win32_Process.cs` (2000 lignes de wrapper WMI généré) :
  `PriorityClass`, `Kill()` et `ProcessorAffinity` couvrent tout nativement.
- Correction du coût en O(n²) de la boucle de collecte (recherche linéaire
  par `.First()` sur la liste interne à chaque ligne retournée).

**Étape 3 — fonctionnalités modernes.** `SetProcessDefaultCpuSets`,
`SetProcessInformation` avec `PROCESS_POWER_THROTTLING_EXECUTION_SPEED`, et
`GetSystemCpuSetInformation` pour exposer l'`EfficiencyClass` de chaque
processeur logique dans l'IHM.

## Pièges connus

- **Masques d'affinité** : jamais d'`int`, jamais de `Math.Pow(2, i)` — qui
  déborde dès `i = 31`. Utiliser `nuint`/`ulong` et des décalages de bits.
- **`ToBinary()`** tronquait le résultat aux 4 derniers caractères, faussant
  l'affichage des cases à cocher au-delà du 4e cœur.
- **Priorité Temps réel** : sans le privilège `SeIncreaseBasePriorityPrivilege`,
  Windows rétrograde silencieusement en Haute, sans lever d'exception.
  Toujours relire la valeur après écriture et signaler toute divergence.
  La même discipline s'applique à l'affinité.
- **Accès refusé** : les accesseurs `ProcessorAffinity` et `PriorityClass`
  lèvent une `Win32Exception` de `NativeErrorCode` 5. Afficher un message
  explicite invitant à relancer en administrateur, pas une erreur générique.
- **Descripteur de sécurité du processus cible** : la lecture comme l'écriture
  de l'affinité et de la priorité y sont soumises.
  `PROCESS_QUERY_LIMITED_INFORMATION` n'y change rien pour un processus d'un
  autre compte — il est plus permissif que `PROCESS_QUERY_INFORMATION` sur les
  processus protégés, pas sur ceux d'autrui. En session non élevée, environ
  **56 %** des entrées sont concernées : ni lisibles, ni modifiables. Ne jamais
  confondre un masque illisible avec un masque vide, et sonder
  `PROCESS_SET_INFORMATION` séparément — quelques processus sont lisibles sans
  être modifiables.
- **Élévation** : c'est l'appartenance du jeton élevé au groupe Administrateurs
  qui débloque la couverture, les DACL des processus l'accordant. Mesuré :
  lecture 137 → 308, écriture 133 → 293 sur 309. `SeDebugPrivilege`, activé par
  `Process.EnterDebugMode()`, n'y ajoute **rien** — il contourne les DACL, déjà
  favorables ici, mais pas la protection PPL. Restent inaccessibles en écriture
  16 processus protégés : `System`, `Secure System`, `Registry`, `smss`,
  `csrss`, `wininit`, `services`, `lsass`, et les composants Defender.
  `EnterDebugMode()` lève une `Win32Exception` en session non élevée : toujours
  l'encadrer.
- **Processus protégés** (`System`, `csrss`, `Registry`…) : `TotalProcessorTime`
  lève. À gérer dès la boucle de collecte, sans polluer les journaux.
- **Groupes de processeurs** : `Process.ProcessorAffinity` ne couvre qu'un seul
  groupe, donc 64 processeurs logiques au maximum. Au-delà, il faut
  `SetThreadGroupAffinity` en P/Invoke.
- **Sélection multiple** : en cas d'échec partiel, appliquer ce qui peut l'être
  et présenter un rapport final. Ne pas annuler ce qui a réussi.
- **Redescente de version et fichier de règles** : toute sauvegarde réécrit
  `rules.json` au format courant (`RuleStore.TrySave` impose
  `CurrentFormatVersion`). Un binaire dont `CurrentFormatVersion` est inférieure
  à celle du fichier le refuse **en bloc** — message explicite, aucune règle
  appliquée, toutes inactives. C'est voulu : mieux vaut ne rien appliquer que
  d'appliquer de travers un fichier écrit par une version plus récente. Mais
  cela veut dire qu'il suffit d'avoir enregistré une seule règle avec la version
  récente pour que le retour en arrière désactive tout.
  **Parade en place** : avant toute réécriture qui change la version de format,
  dans un sens ou dans l'autre, `RuleStore` copie le fichier lu sous
  `rules.v<version lue>.bak` dans le même dossier — c'est bien la version du
  fichier sauvegardé, ce qui reste lisible dans les deux sens. Une seule fois
  par franchissement : la copie existante porte l'état d'origine, qui vaut mieux
  que le plus récent. L'échec de la copie est bloquant, l'ancien fichier restant
  alors intact.
  Le sens descendant est le plus dangereux, et c'est le moins visible : le
  fichier trop récent ayant été refusé en bloc, **aucune règle n'est chargée**,
  si bien que la première sauvegarde de l'utilisateur remplace la totalité de
  ses règles par la seule qu'il vient d'enregistrer. Rien à l'écran ne le
  signale.
  Le refus d'un format trop récent nomme la copie dans son message, quand il en
  existe une que cette version sait lire : c'est le seul moment où l'utilisateur
  en a besoin, et rien jusque-là ne lui apprend qu'elle existe.

## Pistes pour plus tard

- Fenêtre dédiée listant toutes les règles enregistrées avec leur état — active,
  contestée, orpheline, refusée faute de droits — et permettant de les modifier
  ou supprimer sans passer par la tuile. Deviendra nécessaire au-delà d'une
  vingtaine de règles.
- La même fenêtre doit rendre compte d'un fichier de règles refusé pour format
  trop récent : c'est un état du fichier entier, pas d'une règle, et il ne se
  manifeste aujourd'hui que par un message au chargement. Prévoir un moyen
  d'exporter le contenu du fichier refusé — l'utilisateur ne peut rien en faire
  autrement, et c'est le moment où il risque de le perdre.

## Conventions

- Interface en anglais : infobulles, libellés, boutons, messages, titres de
  fenêtres et entrées de menu. Échanges, commentaires du code et messages de
  commit en français.
- Un commit par étape du plan, avec l'application en état de marche.
- **Aucune sonde n'écrit ni ne supprime dans `%APPDATA%\ProcessAffinity`.** Ce
  dossier contient la configuration de l'utilisateur, pas un bac à sable.
  `RuleStore` expose `RedirectTo`, et `RuleEngine` le point d'entrée
  `RedirectStoreTo(chemin)` qui redirige le stockage et vide le cache : toute
  vérification touchant au fichier de règles passe par cette redirection, vers
  un répertoire temporaire créé pour l'essai et supprimé après. La redirection
  est posée avant tout autre appel, et la sonde s'arrête si `RuleEngine.FilePath`
  ne pointe pas dans ce répertoire. Sauvegarder puis restaurer le fichier réel
  ne vaut pas : cela protège tant que personne n'oublie, et laisse le dossier
  vide pendant l'essai.
- Ne pas élargir le périmètre d'une étape sans validation explicite.
- La description de chaque PR énonce la demande d'origine avant la liste des
  modifications. Toute modification non demandée explicitement y est signalée
  comme telle.
- Chaque branche est créée depuis `main` à jour, jamais depuis une branche de
  travail en cours. Une PR basée sur une autre branche se ferme automatiquement
  quand celle-ci est fusionnée, et doit alors être recréée.
- **Les fusions se font en squash**, ce qui réécrit l'identité du commit : `main`
  reçoit un commit neuf, sans lien de parenté avec celui de la branche. Un commit
  apporté d'une autre branche — pour en récupérer un correctif avant sa fusion —
  ne sera donc **jamais** reconnu comme ancêtre commun une fois cette branche
  fusionnée. Les mêmes lignes arrivent des deux côtés sans base commune : il faut
  s'attendre au conflit et le résoudre, non compter sur une fusion silencieuse.
  Corollaire : **une seule PR ouverte à la fois sur un même fichier**. Deux
  branches qui touchent le même fichier finiront par se le disputer, et la
  seconde paiera la fusion de la première.

## Versionnement

À chaque évolution, incrémenter `AssemblyVersion` et `FileVersion` dans
`ProcessAffinityUI.csproj` **avant d'ouvrir la PR** :

- **majeur** : une étape du plan achevée ;
- **mineur** : une sous-étape ou une fonctionnalité ;
- **correctif** : une correction de bug.

L'incrément fait partie du même commit que la modification qu'il accompagne.
La version affichée dans le titre de la fenêtre principale est lue depuis
l'assembly (`Assembly.GetExecutingAssembly().GetName().Version`) — jamais une
constante en dur dans le code.
