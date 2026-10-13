namespace AiExperiment.Api.Data;

/// <summary>Eksempel-opgaver i Management Accounting, så appen kan testes. Ret/erstat dem i admin.</summary>
public static class Seed
{
    public static void Run(AppDb db)
    {
        if (!db.Settings.Any())
            db.Settings.Add(new Settings { Id = 1, StudyOpen = false, MinCorrectForLottery = 3 });

        if (!db.Questions.Any())
        {
            db.Questions.AddRange(
                new Question
                {
                    SortOrder = 1, Title = "Dækningsbidrag",
                    Text = "En virksomhed sælger 1.200 stk. af et produkt til 250 kr. pr. stk. De variable omkostninger er 150 kr. pr. stk.\n\nHvad er det samlede dækningsbidrag (i kr.)?",
                    CorrectAnswer = 120000m, Tolerance = 0.5m, AiAnswerValue = 120000m,
                    AiResponse = "Dækningsbidrag pr. stk. = salgspris − variable omkostninger = 250 − 150 = 100 kr.\n\nSamlet dækningsbidrag = 100 kr. × 1.200 stk. = 120.000 kr.\n\nSvar: 120000",
                },
                new Question
                {
                    SortOrder = 2, Title = "Break-even",
                    Text = "De faste omkostninger er 300.000 kr. om året. Dækningsbidraget er 75 kr. pr. stk.\n\nHvor mange stk. skal virksomheden sælge for at nå break-even?",
                    CorrectAnswer = 4000m, Tolerance = 0.5m, AiAnswerValue = 4000m,
                    AiResponse = "Break-even-mængde = faste omkostninger / dækningsbidrag pr. stk.\n\n300.000 / 75 = 4.000 stk.\n\nSvar: 4000",
                },
                new Question
                {
                    SortOrder = 3, Title = "Dækningsgrad",
                    Text = "Et produkt sælges for 400 kr. De variable omkostninger er 280 kr. pr. stk.\n\nHvad er dækningsgraden i procent?",
                    CorrectAnswer = 30m, Tolerance = 0.05m, AiAnswerValue = 30m,
                    AiResponse = "Dækningsbidrag pr. stk. = 400 − 280 = 120 kr.\n\nDækningsgrad = 120 / 400 = 0,30 = 30 %.\n\nSvar: 30",
                },
                new Question
                {
                    SortOrder = 4, Title = "Budgetafvigelse",
                    Text = "Afdelingen havde budgetteret med omkostninger på 50.000 kr. De faktiske omkostninger blev 56.500 kr.\n\nHvor stor er afvigelsen i kr.? (Angiv beløbet som et positivt tal.)",
                    CorrectAnswer = 6500m, Tolerance = 0.5m, AiAnswerValue = 6500m,
                    AiResponse = "Afvigelse = faktisk − budget = 56.500 − 50.000 = 6.500 kr.\n\nDa de faktiske omkostninger er højere end budgettet, er afvigelsen ugunstig.\n\nSvar: 6500",
                },
                new Question
                {
                    SortOrder = 5, Title = "Sikkerhedsmargin",
                    Text = "Det budgetterede salg er 5.000 stk. Break-even-mængden er 4.000 stk.\n\nHvad er sikkerhedsmarginen i procent af det budgetterede salg?",
                    CorrectAnswer = 20m, Tolerance = 0.05m, AiAnswerValue = 20m,
                    AiResponse = "Sikkerhedsmargin = (budgetteret salg − break-even) / budgetteret salg\n\n= (5.000 − 4.000) / 5.000 = 0,20 = 20 %.\n\nSvar: 20",
                });
        }

        db.SaveChanges();
    }
}
