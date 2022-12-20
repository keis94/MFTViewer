using System;
using MFT;
using MFT.Attributes;
using MFT.Other;

using Serilog;

namespace MFTViewer;

public class MFTViewer
{
    private static Mft? _mft;

    private static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

        if (args.Length != 1)
        {
            Log.Error("Usage ./MFTViewer /path/to/$MFT");
            Environment.Exit(-1);
        }


        string file = args[0];
        _mft = MftFile.Load(file, false);

        Log.Information("FILE records found: {0} (Free records: {1}) File size: {2}", _mft.FileRecords.Count, _mft.FreeFileRecords.Count, _mft.FileSize);

        ProcessRecords(_mft.FileRecords, false, false, "C", ".\\dump", _mft);
    }

    private static void ProcessRecords(Dictionary<string, FileRecord> records, bool includeShort, bool alltimestamp, string bdl, string drDumpDir, Mft mft)
    {
        foreach (var fr in records)
        {
            Log.Verbose(
                "Dumping record with entry: {EntryNumber} at offset {Offset}", $"0x{fr.Value.EntryNumber:X}", $"0x{fr.Value.SequenceNumber:X}");

            if (fr.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                fr.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
            {
                Log.Debug(
                    "Skipping entry # {EntryNumber}, seq #: {SequenceNumber} since it is an extension record", $"0x{fr.Value.EntryNumber:X}", $"0x{fr.Value.SequenceNumber:X}");
                //will get this record via extension records, which were already handled in MFT.dll code
                continue;
            }

            // A useful little thing to find attributes we need to decode
            foreach (var valueAttribute in fr.Value.Attributes)
            {
                if (valueAttribute is not LoggedUtilityStream && valueAttribute is not ReparsePoint && valueAttribute is not LoggedUtilityStream && valueAttribute is not VolumeInformation && valueAttribute is not VolumeName && valueAttribute is not StandardInfo && valueAttribute is not Data && valueAttribute is not FileName && valueAttribute is not IndexRoot && valueAttribute is not IndexAllocation && valueAttribute is not Bitmap && valueAttribute is not ObjectId_ && valueAttribute.GetType().Name != "AttributeList")
                {
                    Log.Information("E/S: {E}-{S}: {A}", fr.Value.EntryNumber, fr.Value.SequenceNumber, valueAttribute.GetType());
                }
            }

            foreach (var attribute in fr.Value.Attributes.Where(t =>
                         t.AttributeType == AttributeType.FileName).OrderBy(t => ((FileName)t).FileInfo.NameType))
            {
                var fn = (FileName)attribute;
                if (includeShort == false &&
                    fn.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }

                var mftr = MFTRecordOut.Create(mft, fr.Value, fn, null, alltimestamp);

                var ads = fr.Value.GetAlternateDataStreams();

                if (String.IsNullOrEmpty(drDumpDir) == false)
                {
                    var data = fr.Value.Attributes.Where(t =>
                        t.AttributeType == AttributeType.Data);

                    foreach (var da in data)
                    {
                        if (da.IsResident == false)
                        {
                            continue;
                        }

                        var outNameR = Path.Combine(drDumpDir, $"{fr.Value.EntryNumber}-{fr.Value.SequenceNumber}_{fn.FileInfo.FileName}.bin");

                        Log.Debug("Saving resident data for {Entry}-{Seq} to {File}", fr.Value.EntryNumber, fr.Value.SequenceNumber, outNameR);

                        File.WriteAllBytes(outNameR, ((Data)da).ResidentData.Data);
                    }
                }
            }
        }
    }
}