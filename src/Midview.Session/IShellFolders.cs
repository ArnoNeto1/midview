namespace Midview.Session;

// The things only the Windows shell can do for the class folder's save
// locations (see docs/superpowers/specs/2026-11-04-class-folder-save-locations-design.md):
// tell Explorer where Downloads is, build a folder's ID list (the form the
// common file dialog keeps its "last folder used" in) and read one back. Behind
// an interface so the tests never touch the developer's real Downloads.
public interface IShellFolders
{
    // Stored exactly as given; throws when the shell refuses.
    void SetDownloads(string path);

    // The folder's absolute ID list, ending in the two zero bytes; null when the
    // shell can't build one.
    byte[]? IdListOf(string folder);

    // The folder path an absolute ID list decodes to; null when the bytes are
    // not a whole ID list or it is not a folder on disk.
    string? PathOf(byte[] idList);
}
