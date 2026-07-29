namespace RonVoice.Core.Config;

public enum PhraseIssueKind
{
    /// <summary>O id da ordem não existe no mapa.</summary>
    UnknownOrder,
    /// <summary>A frase já pertence a outra ordem; aceitar deixaria as duas mudas.</summary>
    Collision,
    /// <summary>A frase já existe nessa mesma ordem. Inofensivo.</summary>
    Duplicate,
    /// <summary>Frase vazia ou só espaços.</summary>
    Empty,
    /// <summary>O arquivo existe mas não pôde ser lido ou não é JSON válido.</summary>
    FileUnreadable,
}

public sealed record PhraseIssue(
    PhraseIssueKind Kind, string OrderId, string Phrase, string Message);
