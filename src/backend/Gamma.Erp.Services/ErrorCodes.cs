namespace Gamma.Erp.Services;

/// <summary>The errors the rules return, in English.</summary>
public static class ErrorCodes
{
    /// <summary>An element's type is not a child of its parent's type, or one without a parent has no top-level type.</summary>
    public const string TypeError = nameof(TypeError);

    /// <summary>An active sibling already has this name.</summary>
    public const string NameError = nameof(NameError);

    /// <summary>The row still has active rows under it, so it cannot be switched off.</summary>
    public const string ChildrenExistError = nameof(ChildrenExistError);

    /// <summary>The category still has elements, so it cannot be moved.</summary>
    public const string ElementsExistError = nameof(ElementsExistError);

    /// <summary>The type has used all the codes of its range.</summary>
    public const string CodeLimitError = nameof(CodeLimitError);

    /// <summary>The document is posted, so it is final: it and its transactions no longer change (a correction is a new document).</summary>
    public const string PostedError = nameof(PostedError);

    /// <summary>Another user holds the document while editing it.</summary>
    public const string LockedError = nameof(LockedError);

    /// <summary>The user, and none of the user's roles, has been given this document type.</summary>
    public const string NotAllowedError = nameof(NotAllowedError);

    /// <summary>The document type's configuration does not have this transaction type, VAT group or transaction configuration.</summary>
    public const string NotConfiguredError = nameof(NotConfiguredError);

    /// <summary>A payment names a document that is not open, or does not cover all that is open on it.</summary>
    public const string OpenItemError = nameof(OpenItemError);

    /// <summary>No scenario fits a transaction, so it cannot be posted.</summary>
    public const string NoScenarioError = nameof(NoScenarioError);

    /// <summary>A scenario's lines, with the partner's line, cannot balance; or a posting's entries do not (debit ≠ credit).</summary>
    public const string UnbalancedError = nameof(UnbalancedError);

    /// <summary>A line lacks a field its document type × transaction type needs.</summary>
    public const string FillError = nameof(FillError);

    /// <summary>A company (a node whose type keeps its own books) must be the root or directly under it, and never under another company.</summary>
    public const string CompanyLevelError = nameof(CompanyLevelError);

    /// <summary>A statutory account posted has no account in another chart the company posts to (its mapping is missing).</summary>
    public const string MappingError = nameof(MappingError);

    /// <summary>A reversal is refused: the document is not posted, partial reversal is not allowed, or more would be reversed than is left.</summary>
    public const string ReversalError = nameof(ReversalError);

    /// <summary>A line takes more of a product than its side has in stock; the message names the places that have it.</summary>
    public const string StockError = nameof(StockError);

    /// <summary>An entry would go to a group account (one with accounts under it); post to an account under it instead.</summary>
    public const string GroupAccountError = nameof(GroupAccountError);

    /// <summary>A sign-up or a new company is refused: the name is taken, the password too short, the country has no pack, a group is missing…</summary>
    public const string SetupError = nameof(SetupError);

    /// <summary>A file is refused: no name, empty, larger than the host allows or of a content type it does not accept.</summary>
    public const string FileError = nameof(FileError);

    /// <summary>A message is refused: the installation has no mail server, or an address is not one, or there are none or too many.</summary>
    public const string MailError = nameof(MailError);

    /// <summary>The document's type is never posted (an offer, a contract): it is kept as a document and makes no entries.</summary>
    public const string NeverPostedError = nameof(NeverPostedError);

    /// <summary>The posting date falls in a closed period of the company.</summary>
    public const string PeriodClosedError = nameof(PeriodClosedError);
}
