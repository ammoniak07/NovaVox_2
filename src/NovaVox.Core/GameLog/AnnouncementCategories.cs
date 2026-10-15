namespace NovaVox.Core.GameLog;

public static class AnnouncementCategories
{
    public const string Route = "route";
    public const string Arrival = "arrival";
    public const string Contracts = "contracts";
    public const string Objectives = "objectives";
    public const string Journal = "journal";
    public const string Jurisdiction = "jurisdiction";
    public const string Zones = "zones";
    public const string Group = "group";
    public const string Invites = "invites";
    public const string Ship = "ship";
    public const string Schemas = "schemas";
    public const string Transfers = "transfers";
    public const string Calibration = "calibration";
    public const string Crimes = "crimes";
    public const string Hangar = "hangar";
    public const string OtherHud = "other_hud";
    public const string AiReply = "ai_reply";

    public static readonly IReadOnlyList<(string Id, string Label)> All = new[]
    {
        (Route, "Direction (route tracée)"),
        (Arrival, "Arrivée à destination"),
        (Contracts, "Contrats"),
        (Objectives, "Objectifs de mission"),
        (Journal, "Entrées du journal"),
        (Jurisdiction, "Juridiction"),
        (Zones, "Zones (armistice, zone restreinte)"),
        (Group, "Groupe (arrivées, départs, connexions, chef, lancement)"),
        (Invites, "Invitations et amis"),
        (Ship, "Canal du vaisseau"),
        (Schemas, "Schémas reçus"),
        (Transfers, "Transferts d'aUEC"),
        (Calibration, "Calibration quantique"),
        (Crimes, "Délits"),
        (Hangar, "Hangar"),
        (OtherHud, "Autres notifications du jeu"),
        (AiReply, "Réponses de l'IA à une question vocale"),
    };
}
