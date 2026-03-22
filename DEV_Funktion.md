# DEV Funktion — PhantomBite Economy

## Zweck
MMO-artiges Wirtschaftssystem für Space Engineers Server. Trader-Blöcke spawnen dynamisch Items mit wechselnden Preisen. Spieler können über AutoTransfer Waren ein- und auslagern ohne die GUI zu verlassen.

## Modul-Übersicht

| Modul | Datei | Beschreibung |
|-------|-------|-------------|
| Core | Session.cs | Session-Component, startet alle Module |
| M00 | CategoryDefinitions.cs | Item-Kategorie Definitionen |
| M01 | Logger.cs | Logging mit Debug-Modus |
| M02 | FileManager.cs | GlobalConfig + Pricelist Verwaltung |
| M03 | Command.cs | Chat-Command Handler (`!sem`) |
| M04 | TraderStoreBlock.cs | TraderStore Block Verwaltung |
| M05 | TraderVendingMachine.cs | VendingMachine Block Verwaltung |
| M06 | EventManager.cs | Dynamic Price + Refresh Timer |
| M07 | AutoTransferModule.cs | AutoTransfer Ladezonen System |

## Economy System

- TraderStore und VendingMachine Blöcke mit Custom Data Config
- Kategorien (Ore, Ingots, Components, Tools, etc.) per Block ein/ausschaltbar
- White/Blacklist pro Block
- Dynamic Price System würfelt Preise aus Pricelists (min/max)
- Preise werden in RAM-Dateien gespeichert und regelmäßig aktualisiert
- Refresh Timer pro Kategorie — gilt für alle Blöcke dieser Kategorie
- Buy System: Spieler verkauft an Store zu Preis × Buy_Margin

## AutoTransfer System

Ziel: Großeinkäufe ohne GUI-Wechsel ermöglichen.

- Spieler dockt Fahrzeug an Ladezonenverbinder an
- Mod erkennt Spieler und reserviert Zone
- Alle anderen Verbinder der Zone werden deaktiviert
- `!sem autotrans in` — Spieler-Inventar → Container
- `!sem autotrans out` — Container → Spieler-Inventar
- `!sem autotrans sort in/out` — Sortierer-basierter Transfer
- Bei Abdocken wird Zone freigegeben

## Commands (`!sem`)

| Command | Admin | Beschreibung |
|---------|-------|-------------|
| `!sem help` | Nein | Alle verfügbaren Commands |
| `!sem autotrans in` | Nein | Transfer Spieler → Container |
| `!sem autotrans out` | Nein | Transfer Container → Spieler |
| `!sem autotrans sort in` | Nein | Sortierer Schiff → Container |
| `!sem autotrans sort out` | Nein | Sortierer Container → Schiff |
| `!sem autotrans stop` | Nein | Transfer stoppen |
| `!sem autotrans scan` | Ja | Zonen neu einlesen |
| `!sem autotrans report` | Ja | Zonenstatus anzeigen |
| `!sem forcerefresh` | Ja | Alle Stores sofort initialisieren |
| `!sem pricelist reload` | Ja | Pricelists neu laden |

## Abhängigkeiten
- **PhantomBite Core** (ID: 3689625814) — AdminChip

## Dateistruktur
```
Phantombite_Economy/
├── modinfo.sbmi
├── metadata.mod
├── DEV_History.md
├── DEV_Funktion.md
├── Data/
│   ├── Blueprints/EncounterDatapads.sbc
│   ├── Cubeblocks/
│   │   ├── Cubeblocks_Economy.sbc
│   │   ├── Cubeblocks_Logistic.sbc
│   │   ├── Cubeblocks_InventoryContainer.sbc
│   │   ├── CubeBlocks_LCDPanels.sbc
│   │   └── Cubeblocks_Category.sbc
│   ├── Items/
│   ├── Factions.sbc
│   ├── Models/
│   ├── Scripts/SpaceEconomy/
│   │   ├── Core/ (Session, ModuleManager, IModule)
│   │   └── Modules/ (M00–M07)
│   └── Textures/
```
