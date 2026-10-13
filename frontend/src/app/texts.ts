/**
 * ALLE TEKSTER DELTAGERNE SER – ret dem her.
 * (Opgaverne og AI-svarene redigeres på admin-siden, ikke her.)
 * Linjeskift i teksterne vises som linjeskift på siden.
 */
export const TEXTS = {
  studyTitle: 'Studie om opgaveløsning',

  waiting: {
    title: 'Eksperimentet er ikke startet endnu',
    body: 'Bliv på denne side. Den går automatisk videre, så snart eksperimentet bliver startet.',
  },

  consent: {
    title: 'Samtykke',
    body:
      'Tak fordi du vil deltage i dette studie om, hvordan man løser opgaver i Management Accounting.\n\n' +
      'Du skal løse fem korte opgaver. Det tager ca. 6–8 minutter.\n\n' +
      'Deltagelse er frivillig, og du kan stoppe når som helst ved at lukke siden. ' +
      'Dine svar gemmes anonymt under et tilfældigt ID og bruges kun til forskning. ' +
      'Vi indsamler ikke dit navn, og der bruges ikke webcam, mikrofon eller skærmoptagelse.',
    accept: 'Jeg accepterer',
  },

  instructions: {
    title: 'Instruktion',
    body:
      'Forestil dig, at du er ansat i økonomiafdelingen. Din manager har brug for dine beregninger til et møde senere i dag.\n\n' +
      'Du får fem opgaver, én ad gangen. Skriv dit svar som et tal i feltet. ' +
      'Du kan bruge knappen "Get AI help", hvis du ønsker hjælp.\n\n' +
      'Du kan ikke gå tilbage til en opgave, når du har trykket "Næste".\n\n' +
      'Deltagere med nok rigtige svar deltager i lodtrækningen om et gavekort.',
  },

  notice: {
    title: 'Om dine svar',
    high: 'Dine svar bliver optaget, og en reviewer vil gennemgå dem.',
    low: 'Kun dine endelige svar bliver indsamlet.',
    continue: 'Start opgaverne',
  },

  task: {
    counter: (pos: number, total: number) => `Opgave ${pos} af ${total}`,
    answerLabel: 'Dit svar (tal)',
    answerPlaceholder: 'Skriv dit svar her',
    aiButton: 'Get AI help',
    aiThinking: 'AI is thinking…',
    aiTitle: 'AI-assistent',
    noBack: 'Du kan ikke gå tilbage til denne opgave bagefter.',
    answerRequired: 'Skriv et svar, før du går videre.',
  },

  /** Overvågnings-elementerne (kun gruppen med høj overvågning). Der optages intet. */
  monitoring: {
    banner: 'Session is being recorded – your activity will be reviewed',
    rec: 'REC',
    actionLogged: 'Action logged',
  },

  manipulation: {
    title: 'Et par afsluttende spørgsmål',
    question: 'Hvor tæt følte du, at dit arbejde blev overvåget?',
    low: 'Slet ikke',
    high: 'Meget tæt',
  },

  background: {
    genderQuestion: 'Køn',
    aiUsageQuestion: 'Hvor ofte bruger du AI i forbindelse med opgaveløsning (på arbejde/i skole)?',
    // Skal matche listerne i backend (ParticipantEndpoints.GenderOptions / AiUsageOptions).
    genderOptions: ['Kvinde', 'Mand', 'Andet', 'Ønsker ikke at svare'],
    aiUsageOptions: ['Aldrig', 'Sjældnere end månedligt', 'Månedligt', 'Ugentligt', 'Dagligt'],
  },

  debrief: {
    title: 'Tak for din deltagelse!',
    body:
      'Formålet med studiet er at undersøge, om man bruger AI-hjælp mindre, når man tror, at ens arbejde bliver overvåget. ' +
      'Deltagerne blev derfor tilfældigt delt i to grupper: Den ene gruppe fik at vide, at deres arbejde blev optaget og gennemgået, ' +
      'den anden fik at vide, at kun de endelige svar blev indsamlet.\n\n' +
      'Der blev ikke optaget noget, og ingen reviewer gennemgår dine svar. Eventuel "REC"-markering var ikke ægte. ' +
      'Vi har kun registreret dine svar og dine klik anonymt – på samme måde for alle deltagere.\n\n' +
      'Vi beder dig om ikke at fortælle andre om formålet, før alle har deltaget.',
    lotteryTitle: 'Lodtrækning om gavekort (valgfrit)',
    lotteryBody:
      'Skriv din e-mail, hvis du vil deltage i lodtrækningen. E-mailen gemmes adskilt fra dine svar og kan ikke kobles til dem.',
    lotteryButton: 'Deltag i lodtrækningen',
    lotteryThanks: 'Tak! Din e-mail er registreret. Du kan nu lukke siden.',
    closeHint: 'Du kan nu lukke siden.',
  },

  common: {
    next: 'Næste',
    networkRetry: 'Forbindelsen er ustabil – prøver igen…',
    error: 'Der skete en fejl. Genindlæs siden for at fortsætte, hvor du slap.',
  },
};
