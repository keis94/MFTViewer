using System;
using System.IO;
using MFT;
using MFT.Attributes;
using MFT.Other;
using RawDiskLib;
using Serilog;
using Boot;
using System.ComponentModel;

namespace MFTViewer;

public class MFTViewer
{
    private static Mft? _mft;

    private static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

        if (args.Length != 1)
        {
            Log.Error("Usage ./MFTViewer driveLetter");
            Environment.Exit(-1);
        }

        long clusterToByteOffset(long clusterOffset, Boot.Boot boot)
        {
            return clusterOffset * boot.SectorsPerCluster * boot.BytesPerSector;
        }

        char driveLetter = args[0][0];
        RawDisk disk = new RawDisk(driveLetter);
        using (Stream stream = disk.CreateDiskStream())
        {
            Boot.Boot boot = new Boot.Boot(stream);
            Log.Information("boot.MftClusterBlockNumber: {0}, boot.SectorsPerCluster: {1}, boot.BytesPerSector: {2}, boot.MftEntrySize: {3}", boot.MftClusterBlockNumber, boot.SectorsPerCluster, boot.BytesPerSector, boot.MftEntrySize, boot.RootDirectoryEntries);

            var MFTOffset = clusterToByteOffset(boot.MftClusterBlockNumber, boot);
            stream.Seek(MFTOffset, SeekOrigin.Begin);

            stream.Seek(0x1c, SeekOrigin.Current);
            Console.WriteLine("{0:X}", stream.Position);

            byte[] blockSizeBytes = new byte[4];
            stream.Read(blockSizeBytes, 0, 4);
            Console.WriteLine("{0:X}", stream.Position);

            stream.Seek(MFTOffset, SeekOrigin.Begin);
            Console.WriteLine("{0:X}", stream.Position);

            var blockSize = BitConverter.ToInt32(blockSizeBytes, 0);
            Log.Information("blocksize: {0}", blockSize);

            var fileBytes = new byte[blockSize];
            stream.Read(fileBytes, 0, blockSize);
            Console.WriteLine("{0:X}", stream.Position);
            Log.Information("filebytes: {0}", fileBytes);

            var record = new FileRecord(fileBytes, 0, false);

            Log.Information("ActualRecordSize: {0}", record.ActualRecordSize);
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(record))
            {
                string name = descriptor.Name;
                object value = descriptor.GetValue(record);
                Console.WriteLine("{0}={1}", name, value);
            }

            foreach (var attr in record.Attributes)
            {
                Console.WriteLine("==================");
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(attr))
                {
                    string name = descriptor.Name;
                    object value = descriptor.GetValue(attr);
                    Console.WriteLine("{0}={1}", name, value);
                }
            }

            foreach (var attribute in record.Attributes.Where(t => t.AttributeType == AttributeType.Data))
            {
                if (attribute.IsResident == false)
                {
                    Console.WriteLine("----- attribute.ContentOffset: {0:X}", attribute.ContentOffset);
                    foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(attribute))
                    {
                        string name = descriptor.Name;
                        object value = descriptor.GetValue(attribute);
                        Console.WriteLine("{0}={1}", name, value);
                    }

                    FileStream file = File.OpenWrite("mft.bin");
                    var data = ((Data)attribute).NonResidentData;
                    var buffer = new byte[boot.BytesPerSector * boot.SectorsPerCluster];
                    foreach (var run in data.DataRuns)
                    {
                        stream.Seek(clusterToByteOffset(run.ClusterOffset, boot), SeekOrigin.Begin);
                        for (ulong i = 0; i < run.ClustersInRun; i++)
                        {
                            var readSize = stream.Read(buffer, 0, buffer.Length);
                            if (readSize != buffer.Length)
                            {
                                Log.Fatal("Failed to read data");
                                Environment.Exit(-1);
                            }
                            file.Write(buffer, 0, readSize);
                        }
                    };
                    file.Dispose();
                }
                else
                {
                    Console.WriteLine("----- resident");
                    foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(attribute))
                    {
                        string name = descriptor.Name;
                        object value = descriptor.GetValue(attribute);
                        Console.WriteLine("{0}={1}", name, value);
                    }
                }
            }

            // ProcessRecords(new Dictionary<string, FileRecord>({ "mft", record }, false, false, "C", @".\dump", _mft))

            // _mft = new Mft(stream, false);
            // Log.Information("FILE records found: {0} (Free records: {1}) File size: {2}", _mft.FileRecords.Count, _mft.FreeFileRecords.Count, _mft.FileSize);
            // ProcessRecords(_mft.FileRecords, false, false, "C", @".\dump", _mft);
        }
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
