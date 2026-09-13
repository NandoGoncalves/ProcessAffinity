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
- **Processus protégés** (`System`, `csrss`, `Registry`…) : `TotalProcessorTime`
  lève. À gérer dès la boucle de collecte, sans polluer les journaux.
- **Groupes de processeurs** : `Process.ProcessorAffinity` ne couvre qu'un seul
  groupe, donc 64 processeurs logiques au maximum. Au-delà, il faut
  `SetThreadGroupAffinity` en P/Invoke.
- **Sélection multiple** : en cas d'échec partiel, appliquer ce qui peut l'être
  et présenter un rapport final. Ne pas annuler ce qui a réussi.

## Conventions

- Échanges et messages d'interface en français.
- Un commit par étape du plan, avec l'application en état de marche.
- Ne pas élargir le périmètre d'une étape sans validation explicite.
- La description de chaque PR énonce la demande d'origine avant la liste des
  modifications. Toute modification non demandée explicitement y est signalée
  comme telle.

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
