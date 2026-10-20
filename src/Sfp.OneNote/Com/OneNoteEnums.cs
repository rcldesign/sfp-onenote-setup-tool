namespace Sfp.OneNote.Com;

/// <summary>What OpenHierarchy creates when the path does not exist (OneNote CreateFileType).</summary>
public enum CreateFileType
{
    None = 0,
    Notebook = 1,
    Folder = 2,
    Section = 3,
}

/// <summary>Page style for CreateNewPage (OneNote NewPageStyle).</summary>
public enum NewPageStyle
{
    Default = 0,
    BlankPageWithTitle = 1,
    BlankPageNoTitle = 2,
}

/// <summary>How deep GetHierarchy walks (OneNote HierarchyScope).</summary>
public enum HierarchyScope
{
    Self = 0,
    Children = 1,
    Notebooks = 2,
    Sections = 3,
    Pages = 4,
}

/// <summary>What GetPageContent includes (OneNote PageInfo). Binary data means image and ink bytes.</summary>
public enum PageInfo
{
    Basic = 0,
    BinaryData = 1,
    Selection = 2,
    BinaryDataSelection = 3,
    FileType = 4,
    All = 7,
}

/// <summary>The XML schema version passed to every call (OneNote XMLSchema). The tool only speaks 2013.</summary>
public enum XmlSchemaVersion
{
    Schema2007 = 0,
    Schema2010 = 1,
    Schema2013 = 2,
    Current = 3,
}
