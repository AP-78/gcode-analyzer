# G-Code-Analyzer

Windows-Programm, das eine fertig gesliced G-Code-Datei liest und zeigt, was tatsächlich gedruckt wird: Wände, Schalen, Infill, Support und Druckstatistik, dazu eine 3D-Ansicht.

## Language

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
