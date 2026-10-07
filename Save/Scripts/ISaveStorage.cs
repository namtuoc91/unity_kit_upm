namespace Raccoon.Save
{
    /// <summary>
    /// Where GameSave reads / writes its JSON. FileSaveStorage is the local implementation,
    /// a cloud storage (Firestore, HTTP...) can implement this later without changing game code.
    /// Called from the main thread only, Write must finish synchronously (the OS may kill the app right after a pause).
    /// </summary>
    public interface ISaveStorage
    {
        //False when there is no valid save yet (first launch, or every copy is corrupt)
        bool TryRead(out string json);

        //Throws on failure (disk full...), GameSave keeps the data in memory and retries on the next save
        void Write(string json);

        void Delete();
    }
}
