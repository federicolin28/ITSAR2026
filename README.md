# ITSAR 2026 – UFS03 · Crystal Survivors

Progetto di corso di UFS03 – C# for Unity – Coding Fundamentals Vibe Coding (ITS Academy Angelo Rizzoli, edizione 2026).

**Crystal Survivors** è un action tower defense con attacco automatico, in stile *Vampire Survivors*:
il giocatore si muove in un'arena, le sue armi colpiscono da sole il nemico più vicino e deve
proteggere il cristallo al centro dalle ondate di nemici. Uccidendo i nemici raccoglie gemme,
sale di livello, sceglie potenziamenti e costruisce torri difensive.

Il gioco è costruito una lezione alla volta: ogni lezione aggiunge un sistema che corrisponde
agli argomenti teorici del corso.

## Requisiti

- Unity **6000.6.0f1** (Unity 6), template Universal 3D (URP)
- Pacchetti: Input System, AI Navigation, uGUI (già presenti nel progetto)
- Git e un client a scelta (GitHub Desktop va benissimo)

## Come usare la repository

Ogni lezione ha **due tag**:

| Tag | Contenuto |
|---|---|
| `lezione-NN-inizio` | Il punto di partenza del laboratorio: scena e asset pronti, codice da generare con l'AI |
| `lezione-NN-fine` | La soluzione di riferimento del docente |

Per lavorare su una lezione:

```bash
git checkout -b mia-lezione-03 lezione-03-inizio
```

Per confrontare il tuo lavoro con la soluzione:

```bash
git diff lezione-03-fine
```

La consegna del laboratorio di ogni lezione è in [`Docs/Lezioni`](Docs/Lezioni).

## Le lezioni

| # | Lezione | Cosa si aggiunge al gioco |
|---|---|---|
| 01 | Kickoff: Unity, Git e vibe coding | Progetto, repository, cristallo che ruota |
| 02 | Anatomia di Unity e lettura del C# | Arena, prefab, ciclo di vita del MonoBehaviour |
| 03 | Prompt engineering e Input System | Movimento del player, camera, menu di pausa |
| 04 | Fisica, collisioni e raycast | Vita, trappole, gemme, targeting automatico, prima arma |
| 05 | Ereditarietà, polimorfismo, eventi | Armi polimorfiche, torri, HUD a eventi |
| 06 | ScriptableObject | Dati di armi, nemici e ondate; livelli e potenziamenti |
| 07 | Memoria e performance | Object pooling di nemici, proiettili e gemme |
| 08 | IA dei nemici: FSM e NavMesh | Nemici con macchina a stati e pathfinding |
| 09 | Scene, Singleton e architettura | Menu, game over, gestione delle scene |
| 10 | Ripasso e preparazione all'esame | Pulizia, documentazione, sistema di pickup |

## Struttura

```
Assets/_Project/
  Scenes/      scene del gioco
  Scripts/     codice C#, diviso per sistema
  Prefabs/     prefab riutilizzabili
  Materials/   materiali (solo colori: il gioco usa primitive)
  Data/        ScriptableObject (dalla lezione 06)
  Input/       asset dell'Input System (dalla lezione 03)
Docs/
  Lezioni/     consegna del laboratorio di ogni lezione
```

## Comandare Unity dal terminale (Claude Unity Bridge)

Il progetto include il pacchetto `Packages/com.claude.unitybridge`: quando Unity è aperto, il comando
`unity.bat` nella radice della repository permette di compilare, leggere la Console, ispezionare e
modificare la scena, provare il gioco e lanciare i test dal terminale. È pensato soprattutto per gli
assistenti AI che eseguono comandi (Claude Code, Copilot in modalità agent, Gemini CLI…), ma si può
usare anche a mano, o dalla dashboard nel browser (menu **Tools > Claude Bridge**).

```bash
./unity.bat refresh     # compila e mostra gli errori
./unity.bat run 5       # prova il gioco per 5 secondi e riporta errori e warning
./unity.bat tree        # gerarchia della scena
./unity.bat help        # tutti i comandi
```

Le istruzioni per l'assistente sono in [`UNITY_BRIDGE.md`](UNITY_BRIDGE.md).
