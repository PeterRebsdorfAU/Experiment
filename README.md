# AI-overvågningseksperiment

Webapp til eksperimentet: *Bruger folk AI mindre, når de tror, de bliver overvåget?*

- **Backend:** C# / ASP.NET Core 10 (minimal API) + EF Core. PostgreSQL (Neon) i produktion, SQLite lokalt.
- **Frontend:** Angular 20. Bygges ind i backend'en, så alt kører som **én tjeneste på én URL**.
- **Deltager-link:** `https://<din-app>.onrender.com/`
- **Admin:** `https://<din-app>.onrender.com/admin`

## Forløbet for deltageren

1. *(Venteside, hvis eksperimentet ikke er startet. Den går automatisk videre, når admin trykker Start.)*
2. Samtykke → "Jeg accepterer"
3. Fælles instruktion
4. **Tilfældig fordeling på serveren**: ny deltager kommer i den mindste gruppe, og ved lige mange trækkes der lod.
   Grupperne er derfor **aldrig mere end 1 fra hinanden**, også efter sletning af deltagere.
5. Gruppebesked: *høj* = "Dine svar bliver optaget …" / *lav* = "Kun dine endelige svar bliver indsamlet."
6. Opgaverne, én pr. skærm (tekst, talsvar, **Get AI help**, Næste). Man kan ikke gå tilbage, heller ikke ved genindlæsning eller med browserens tilbage-knap.
7. Manipulationstjek (1–7)
8. Baggrund (køn, AI-brug)
9. Debriefing + valgfri e-mail til lodtrækning

**Høj overvågning** ser på opgaveskærmene: rød bjælke "Session is being recorded …", blinkende **● REC** med tæller og
små "Action logged"-beskeder ved klik og tastetryk. Der optages intet, og der spørges ikke om webcam eller skærm.

**Get AI help** viser "AI is thinking…" i 1,6 sek. og derefter det forudskrevne svar, som redigeres i admin.

## Hvad logges

| Eksport | Indhold |
|---|---|
| `participants.csv` | Én række pr. deltager: anonymt ID, gruppe, start/slut, manipulationstjek, baggrund, antal rigtige, AI-klik i alt, antal svar = AI's svar |
| `tasks.csv` | Én række pr. deltager × opgave: **antal AI-klik, AI brugt (0/1), tidspunkt for første klik, sekunder til første klik**, svar (rå + tal), korrekt (0/1), **identisk med AI's svar (0/1)**, tid brugt |
| `events.csv` | **Rå hændelseslog** med tidsstempler: `task_shown`, `ai_help_click`, `ai_response_shown`, `answer_change` (feltets indhold), `answer_paste` (indsat tekst), `ai_text_copy` (kopieret fra AI-svaret), `tab_hidden`/`tab_visible` og `window_blur`/`window_focus` (skiftede væk, fx til ChatGPT), `answer_submitted`, … |
| `lottery.csv` | E-mails, **separat og uden deltager-ID**. Kun dato, ikke klokkeslæt, så de ikke kan matches på tid. Kolonnen `eligible` siger om kravet til antal rigtige var opfyldt. |
| `all.xlsx` | Deltagere + opgaver + hændelser i ét Excel-ark (**uden** e-mails) |

Tidspunkter er dansk tid. Test-deltagere (fra test-links) har `is_test = 1` og bør filtreres fra i analysen.

## Admin-siden (`/admin`)

- **▶ Start / Stop**: åbner og lukker for nye deltagere. Deltagere der er i gang, kan altid gøre det færdigt.
- Live-tal og procentfordeling pr. gruppe og liste over deltagere (opdateres hvert 5. sek.)
- **Slet** til højre på hver deltager: sletter deltagerens svar og hændelseslog. Fx en elev, der faldt fra
  halvvejs; næste nye deltager kommer så i den gruppe, der mangler én.
- **Spørgsmål**: tilføj, redigér, slet og ændr rækkefølge. Pr. spørgsmål: titel, tekst, korrekt svar, tolerance, AI-svartekst og AI'ens talsvar.
  Hver deltager låses til de spørgsmål, der fandtes, da de startede.
- **Test-links** (`/?test=high` og `/?test=low`) til at prøve begge versioner, også når eksperimentet er lukket.
- **Tekster**: samtykke, instruktion, gruppebeskeder, overvågnings-tekster, manipulationstjek og debriefing kan rettes
  direkte på admin-siden. "Gendan standard" henter den oprindelige tekst.
- Eksport, lodtrækningskrav og "Slet deltagerdata" (efter pilottest).

Standardteksterne står i [`frontend/src/app/texts.ts`](frontend/src/app/texts.ts). Rettelser fra admin gemmes i databasen og lægges ovenpå.

## Kør lokalt

```bash
cd backend && dotnet run
```
```bash
cd frontend && npm start
```
Åbn http://localhost:4200 (deltager) og http://localhost:4200/admin (adgangskode lokalt: `admin`).
Lokalt bruges filen `backend/experiment.db` (SQLite). Slet den for at starte helt forfra.

## Udgiv på Render + Neon

1. **Neon:** Opret et nyt projekt, fx `ai-eksperiment`, region *Frankfurt*. Kopiér connection string med **"Pooled connection"** slået til.
2. **GitHub:** Læg denne mappe i et (privat) repository.
3. **Render:** *New → Blueprint →* vælg repo'et. Render læser `render.yaml`.
4. Udfyld de to hemmelige variabler:
   - `ConnectionStrings__Default` = Neon-strengen (`postgresql://…` virker direkte)
   - `Admin__Password` = en lang, tilfældig adgangskode
5. Vent på build (~5 min). Tabellerne og de 5 eksempel-opgaver oprettes automatisk ved første start.
6. Gå til `/admin`, ret opgaverne, prøv begge test-links, tryk **Slet deltagerdata**, og send så deltager-linket ud.

### Stabilitet ved 50 samtidige deltagere

- Belastningen er lille (få forespørgsler pr. sekund). Testet lokalt med 100 samtidige starter uden fejl (fordelt præcis 50/50).
- **Renders gratis plan går i dvale** efter 15 min uden trafik, og opstart tager ~1 min. Brug enten **Starter-planen** i
  eksperimentperioden (skift `plan: free` til `plan: starter`), eller åbn admin-siden et par minutter før start.
- Browseren prøver automatisk igen ved netværksfejl, og hændelser gemmes i kø i browseren, til de er sendt.
  Dobbeltklik og samtidige forespørgsler afvises på serveren (optimistisk låsning), så tællere ikke bliver forkerte.
- Genindlæser en deltager siden, fortsætter de præcis hvor de var (samme opgave, REC-tælleren kører videre).

### Bemærk ved ændringer i datamodellen

Databasen oprettes med `EnsureCreated` (ingen migrationer). Ændrer du entiteterne i `backend/Data/Entities.cs`,
efter der er data i Neon, skal tabellerne slettes/oprettes på ny. Eksportér altid først.
