# Transkript: G-Code-Analyzer in 2 Minuten

Sprecherstimme: Piper, Stimme „Thorsten“ (KI-generiert). In eckigen Klammern steht, was im Bild zu sehen ist.

## 1. Einstieg

[Übersicht: 3D-Ansicht des ganzen Drucks]

Der Slicer zeigt, was eingestellt ist. Was davon tatsächlich im G-Code landet, zeigt er nicht. Genau dafür gibt es den G-Code-Analyzer: ein kostenloses Windows-Programm, das fertige G-Code-Dateien in 3D darstellt und mit den Einstellungen vergleicht.

## 2. Favoriten

[Rahmen um die Favoriten oben rechts]

Oben rechts stehen die Favoriten, die wichtigsten Werte auf einen Blick: Wandschleifen, Schalen oben und unten sowie das Infill. Im Beispiel sind zwei obere Schalen eingestellt, dazu eine Mindestdicke von 0,8 Millimetern. Bei 0,24 Millimetern Layerhöhe druckt der Slicer deshalb vier Layer, doppelt so viele wie gedacht.

[Stern neben „Druckzeit“ wird angeklickt, die Zeile erscheint bei den Favoriten]

Jede weitere Zeile lässt sich mit dem Stern anheften, zum Beispiel die Druckzeit.

## 3. Layer für Layer

[Layer-Regler läuft von unten nach oben, der Druck wächst]

Mit dem Layer-Regler geht es Schicht für Schicht durch den Druck. Jede Pfadart hat ihre eigene Farbe und lässt sich einzeln ausblenden.

## 4. Wände und Schalen

[Seitenleiste: Wände und Schalen]

Bei den Wänden vergleicht die App die eingestellte Wandstärke mit den tatsächlich gedruckten Schleifen. Gewollte Abweichungen des Slicers erscheinen grau, echte Unterschreitungen rot. Die Schalenschichten werden auf einem Millimeter-Raster nachgemessen, dünne Stellen markiert die 3D-Ansicht mit einem Kreuz.

## 5. Infill und Support

[Seitenleiste: Infill, Support, Datei-Info]

Für Infill und Support zeigt die App Muster, Dichte und den Anteil an Filament und Druckzeit. Ist Support aktiviert, aber keiner erzeugt worden, gibt es einen Hinweis.

## 6. Einfärbung

[Einfärbung wechselt: Pfadart, Geschwindigkeit, Volumenstrom, Layerzeit]

Die Einfärbung lässt sich umschalten: nach Pfadart, Geschwindigkeit, Volumenstrom oder Layerzeit. So sieht man sofort, wo der Drucker am Limit läuft.

## 7. Zeit pro Layer

[Diagramm unten, ein Balken wird angeklickt]

Das Diagramm zeigt die Zeit pro Layer und warnt bei Layern, die zu schnell fertig sind. Ein Klick auf einen Balken springt direkt zu diesem Layer.

## 8. Slicer und Download

[Übersicht, langsamer Zoom heraus]

Gelesen werden Dateien aus OrcaSlicer, Bambu Studio und PrusaSlicer, aus Cura die Grundwerte. Getestet ist bisher OrcaSlicer. Die App ist eine einzelne Datei, braucht keine Installation und kein Internet. Den Download gibt es kostenlos auf GitHub, der Link steht in der Beschreibung.

## Schlussbild (ohne Sprecher)

- Download: github.com/AP-78/gcode-analyzer
- Mehr dazu: ap78.de/g-code-analyzer-was-der-slicer-wirklich-druckt
- Modell: „Playstation Icons Lights Lamp (LED Remix)“ von PossTheBoss, Remix von „Playstation Icons Lights“ von aelkaim, CC BY-NC-SA 4.0
- Icons: Font Awesome Free, CC BY 4.0
- Stimme: KI-generiert mit Piper (Thorsten)
- Musik: „Horizons“ von Alex Jones und Xander Jones (YouTube-Audio-Mediathek)
