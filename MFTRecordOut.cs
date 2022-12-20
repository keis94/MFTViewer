using MFT;
using MFT.Attributes;
using MFT.Other;

using System.Text;

namespace MFTViewer;

public class MFTRecordOut
{
    public uint EntryNumber { get; set; }
    public ushort SequenceNumber { get; set; }

    public uint ParentEntryNumber { get; set; }
    public short? ParentSequenceNumber { get; set; }

    public bool InUse { get; set; }
    public string? ParentPath { get; set; }
    public string? FileName { get; set; }

    public string? Extension { get; set; }

    public bool IsDirectory { get; set; }
    public bool HasAds { get; set; }
    public bool IsAds { get; set; }

    public ulong FileSize { get; set; }

    public DateTimeOffset? Created0x10 { get; set; }
    public DateTimeOffset? Created0x30 { get; set; }

    public DateTimeOffset? LastModified0x10 { get; set; }
    public DateTimeOffset? LastModified0x30 { get; set; }

    public DateTimeOffset? LastRecordChange0x10 { get; set; }
    public DateTimeOffset? LastRecordChange0x30 { get; set; }

    public DateTimeOffset? LastAccess0x10 { get; set; }

    public DateTimeOffset? LastAccess0x30 { get; set; }

    public long UpdateSequenceNumber { get; set; }
    public long LogfileSequenceNumber { get; set; }

    public int SecurityId { get; set; }

    public string? ZoneIdContents { get; set; }
    public StandardInfo.Flag SiFlags { get; set; }
    public string? ObjectIdFileDroid { get; set; }
    public string? ReparseTarget { get; set; }
    public int ReferenceCount { get; set; }
    public NameTypes NameType { get; set; }
    public string? LoggedUtilStream { get; set; }
    public bool Timestomped { get; set; }
    public bool uSecZeros { get; set; }
    public bool Copied { get; set; }

    public int FnAttributeId { get; set; }
    public int OtherAttributeId { get; set; }

    public static MFTRecordOut Create(Mft mft, FileRecord fr, FileName fn, AdsInfo? adsinfo, bool alltimestamp)
    {
        var mftr = new MFTRecordOut
        {
            EntryNumber = fr.EntryNumber,
            FileName = fn.FileInfo.FileName,
            InUse = fr.IsDeleted() == false,
            ParentPath = mft.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey()),
            SequenceNumber = fr.SequenceNumber,
            IsDirectory = fr.IsDirectory(),
            ParentEntryNumber = fn.FileInfo.ParentMftRecord.MftEntryNumber,
            ParentSequenceNumber = fn.FileInfo.ParentMftRecord.MftSequenceNumber,
            NameType = fn.FileInfo.NameType,
            FnAttributeId = fn.AttributeNumber
        };

        if (mftr.IsDirectory == false)
        {
            mftr.Extension = Path.GetExtension(mftr.FileName);

            var data = fr.Attributes.FirstOrDefault(t => t.AttributeType == AttributeType.Data);

            if (data != null)
            {
                mftr.OtherAttributeId = data.AttributeNumber;
            }
        }

        mftr.FileSize = fr.GetFileSize();

        if (adsinfo != null)
        {
            mftr.FileName = $"{mftr.FileName}:{adsinfo.Name}";
            mftr.FileSize = adsinfo.Size;
            try
            {
                mftr.Extension = Path.GetExtension(adsinfo.Name);
            }
            catch (Exception)
            {
                //sometimes bad chars show up
            }

            if (adsinfo.Name == "Zone.Identifier")
            {
                if (adsinfo.ResidentData != null)
                {
                    mftr.ZoneIdContents = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(adsinfo.ResidentData.Data);
                }
                else
                {
                    mftr.ZoneIdContents = "(Zone.Identifier data is non-resident)";
                }
            }
        }

        mftr.ReferenceCount = fr.GetReferenceCount();

        mftr.LogfileSequenceNumber = fr.LogSequenceNumber;

        var oid = (ObjectId_)fr.Attributes.SingleOrDefault(t =>
            t.AttributeType == AttributeType.VolumeVersionObjectId);

        if (oid != null)
        {
            mftr.ObjectIdFileDroid = oid.ObjectId.ToString();
        }

        var lus = (LoggedUtilityStream)fr.Attributes.FirstOrDefault(t =>
            t.AttributeType == AttributeType.LoggedUtilityStream);

        if (lus != null)
        {
            mftr.LoggedUtilStream = lus.Name;
        }

        var rp = fr.GetReparsePoint();
        if (rp != null)
        {
            mftr.ReparseTarget = rp.SubstituteName.Replace(@"\??\", "");
        }

        var si = (StandardInfo)fr.Attributes.SingleOrDefault(t =>
            t.AttributeType == AttributeType.StandardInformation);

        if (si != null)
        {
            mftr.UpdateSequenceNumber = si.UpdateSequenceNumber;

            mftr.Created0x10 = si.CreatedOn;
            mftr.LastModified0x10 = si.ContentModifiedOn;
            mftr.LastRecordChange0x10 = si.RecordModifiedOn;
            mftr.LastAccess0x10 = si.LastAccessedOn;

            mftr.Copied = si.ContentModifiedOn < si.CreatedOn;

            if (alltimestamp || fn.FileInfo.CreatedOn != si.CreatedOn)
            {
                mftr.Created0x30 = fn.FileInfo.CreatedOn;
            }

            if (alltimestamp ||
                fn.FileInfo.ContentModifiedOn != si.ContentModifiedOn)
            {
                mftr.LastModified0x30 = fn.FileInfo.ContentModifiedOn;
            }

            if (alltimestamp ||
                fn.FileInfo.RecordModifiedOn != si.RecordModifiedOn)
            {
                mftr.LastRecordChange0x30 = fn.FileInfo.RecordModifiedOn;
            }

            if (alltimestamp ||
                fn.FileInfo.LastAccessedOn != si.LastAccessedOn)
            {
                mftr.LastAccess0x30 = fn.FileInfo.LastAccessedOn;
            }

            mftr.SecurityId = si.SecurityId;

            mftr.SiFlags = si.Flags;

            if (mftr.Created0x30.HasValue && mftr.Created0x10?.UtcTicks < mftr.Created0x30.Value.UtcTicks)
            {
                mftr.Timestomped = true;
            }

            if (mftr.Created0x10?.Millisecond == 0 || mftr.LastModified0x10?.Millisecond == 0)
            {
                mftr.uSecZeros = true;
            }
        }
        else
        {
            //no si, so update FN timestamps
            mftr.Created0x30 = fn.FileInfo.CreatedOn;
            mftr.LastModified0x10 = fn.FileInfo.ContentModifiedOn;
            mftr.LastRecordChange0x10 = fn.FileInfo.RecordModifiedOn;
            mftr.LastAccess0x10 = fn.FileInfo.LastAccessedOn;
        }

        return mftr;
    }
}

public class FileListEntry
{
    public FileListEntry(MFTRecordOut r)
    {
        FullPath = $"{r.ParentPath}\\{r.FileName}";
        Extension = r.Extension;
        IsDirectory = r.IsDirectory;
        FileSize = r.FileSize;
        Created0x10 = r.Created0x10;
        LastModified0x10 = r.LastModified0x10;
    }

    public string FullPath { get; set; }
    public string? Extension { get; set; }

    public bool IsDirectory { get; set; }
    public ulong FileSize { get; set; }
    public DateTimeOffset? Created0x10 { get; set; }
    public DateTimeOffset? LastModified0x10 { get; set; }
}


