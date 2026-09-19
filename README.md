# Phantombite Economy

Wirtschaftssystem für Space-Engineers-Server. Das eingebaute Economy-System von Space Engineers ist nie fertig geworden und
fest im Spiel verdrahtet. Dieser Mod arbeitet deshalb mit Umwegen: **Trader-Blöcke** (Store- und Vending-Blöcke) werden über ihre
**Custom Data** konfiguriert und mit Waren zu wechselnden Preisen bestückt.

## Funktionen
- **TraderStore** (Kaufen und Verkaufen) und **VendingMachine** (nur Verkaufen) als Block-Typen
- Konfiguration jedes Blocks über die Custom Data, mit automatisch eingetragener Vorlage und Prüfung auf Fehler
- **Kategorien** (Erze, Barren, Komponenten, Werkzeuge u. a.) pro Block ein- und ausschaltbar, dazu Weiß- und Schwarzliste
- **Dynamische Preise:** Preise werden aus Preislisten (Minimum/Maximum) gewürfelt und regelmäßig neu berechnet,
  ein Refresh-Timer gilt je Kategorie für alle Blöcke
- Ankaufspreis = Preis × Buy-Marge
- Reagiert auf die Server-Last (über den Phantombite Core)
- Lässt sich mit **Phantombite AutoTransfer** kombinieren, um Waren ohne Inventar-Fenster ein- und auszulagern

## Commands (nur Admin)
```
!pbc economy <command>
```
| Command | Beschreibung |
|---|---|
| `forcerefresh` | Alle Stores sofort neu bestücken und Preise neu würfeln |
| `pricelist reload` | Preislisten neu laden |

## Voraussetzungen
- **Phantombite Core** (Commands, Log-Level; einige Blöcke benötigen den `AdminChip`)

Workshop-ID: 3728099479
