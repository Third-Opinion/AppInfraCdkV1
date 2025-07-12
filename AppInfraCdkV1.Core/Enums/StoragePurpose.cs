namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// Storage purposes for S3 buckets, EFS, and caching systems
/// </summary>
public enum StoragePurpose
{
    /// <summary>
    /// Application data and assets
    /// </summary>
    App,
    
    /// <summary>
    /// Document storage (PDFs, files, etc.)
    /// </summary>
    Documents,
    
    /// <summary>
    /// User uploaded content
    /// </summary>
    Uploads,
    
    /// <summary>
    /// Backup and archive storage
    /// </summary>
    Backups,
    
    /// <summary>
    /// Caching layer
    /// </summary>
    Cache,
    
    /// <summary>
    /// Session storage
    /// </summary>
    Session
}