# G-Code-Analyzer

Windows-Programm, das eine fertig gesliced G-Code-Datei liest und zeigt, was tatsächlich gedruckt wird: Wände, Schalen, Infill, Support und Druckstatistik, dazu eine 3D-Ansicht.

## Language

Die Begriffe gelten für alle deutschen Texte (Oberfläche, Doku, Tickets). Code ist englisch benannt; dort sind die englischen Übersetzungen der Begriffe erlaubt (z. B. `Travel` für Fahrweg, `Loops` für Wandschleifen), die _Avoid_-Listen betreffen nur deutsche Texte.

### Datei und Einstellungen

**G-Code-Datei**:
Vom Slicer erzeugte Druckdatei mit Bewegungsbefehlen, Kommentar-Markierungen und angehängten Slicer-Einstellungen.
_Avoid_: Druckdatei, Job

**Slicer-Einstellung**:
Ein Wert aus dem Einstellungsblock der G-Code-Datei, so wie der Benutzer ihn im Slicer gesetzt hat.
_Avoid_: Config, Parameter

**Layer**:
Eine Druckebene auf fester Z-Höhe, begrenzt durch zwei Layer-Wechsel.
_Avoid_: Schicht (außer in "Schalenschicht"), Ebene

### Pfade

**Pfad-Kategorie**:
Die Rolle eines extrudierten Abschnitts laut Slicer-Markierung, z. B. Außenwand, Innenwand, Sparse-Infill, Massiv-Infill, Oberfläche, Unterseite, Lückenfüllung, Brücke, Support.
_Avoid_: Feature-Typ, Linientyp

**Fahrweg**:
Bewegung ohne Extrusion.
_Avoid_: Travel, Leerfahrt

**Retract**:
Zurückziehen des Filaments vor einem Fahrweg, per negativem Vorschub oder Firmware-Befehl; mehrere Rückzüge hintereinander zählen als einer.
_Avoid_: Rückzug, Einzug

**Z-Hop**:
Kurzes Anheben der Düse während eines Fahrwegs, damit sie den Druck nicht streift.
_Avoid_: Z-Lift, Anheben

**Volumenstrom**:
Extrudiertes Kunststoffvolumen pro Zeit in mm³/s.
_Avoid_: Flow, Durchfluss

**Massive Füllung**:
Alle Pfade eines Layers, die eine Fläche lückenlos füllen: Massiv-Infill, Oberfläche, Unterseite und Brücke.
_Avoid_: Solid Fill, Vollfüllung

### Wände

**Wandschleife**:
Ein geschlossener Wandpfad (Außen- oder Innenwand) um eine Kontur.
_Avoid_: Perimeter, Loop

**Soll-Wandstärke**:
Wandstärke, die sich aus den Slicer-Einstellungen ergibt: Anzahl Wandschleifen mal deren Linienbreiten.

**Ist-Wandstärke**:
Wandstärke, die sich aus den tatsächlich gedruckten Wandschleifen eines Layers ergibt.

**Erwartete Abweichung**:
Ist-Wandstärke unter dem Soll, die der Slicer absichtlich so erzeugt, z. B. nur eine Wand auf obersten Flächen oder Lückenfüllung an dünnen Stellen.

**Wand-Unterschreitung**:
Ist-Wandstärke unter dem Soll, die keine erwartete Abweichung ist.
_Avoid_: Wandfehler

### Schalen

**Schalenschicht**:
Ein massiv gefüllter Layer an der Ober- oder Unterseite eines Bereichs.
_Avoid_: Deckschicht, Top/Bottom Layer

**Eingestellte Schalenschichten**:
Anzahl und Mindestdicke der Schalenschichten laut Slicer-Einstellung, getrennt für oben und unten.

**Effektive Schalenschichten**:
Anzahl, die der Slicer aus den eingestellten Werten tatsächlich verwendet (das Maximum aus Anzahl und Dicke geteilt durch Layerhöhe).

**Gemessene Schalenschichten**:
Anzahl massiver Layer, die in der G-Code-Datei an einer Stelle tatsächlich übereinander gedruckt werden.

**Dünne Schalenstelle**:
Ort über (oder unter) Sparse-Infill, an dem weniger gemessene als effektive Schalenschichten übereinanderliegen. Durchgehend massive Bereiche haben keine Schale und zählen nicht.

### Support

**Support-Widerspruch**:
Support ist in den Slicer-Einstellungen aktiviert, die G-Code-Datei enthält aber keine Support-Pfade.

### Oberfläche

**Favorit**:
Eine angeheftete Zeile der Seitenleiste, die zusätzlich ganz oben steht; gilt für jede geöffnete G-Code-Datei.
_Avoid_: Lesezeichen, Pin
