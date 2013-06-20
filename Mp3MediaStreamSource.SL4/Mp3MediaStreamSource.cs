//-----------------------------------------------------------------------
// <copyright file="Mp3MediaStreamSource.cs" company="Larry Olson">
// (c) Copyright Larry Olson.
//
// Changes to support duration and streaming (i.e. non-seekable) content
// (c) Copyright 2010 Rdio.
//
// This source is subject to the Microsoft Public License (Ms-PL)
// See http://code.msdn.microsoft.com/ManagedMediaHelpers/Project/License.aspx
// All other rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Media;
using MediaParsers;

[module: SuppressMessage("Microsoft.Naming",
    "CA1709:IdentifiersShouldBeCasedCorrectly",
    Scope = "type",
    Target = "Media.Mp3MediaStreamSource",
    MessageId = "Mp",
    Justification = "Mp is not a two letter acyonym but is instead part of Mp3")]

/**
 * Presents a byte stream as mp3 frames for the Media Element.  
 * Based on ManagedMediaHelpers code sample from http://code.msdn.microsoft.com/ManagedMediaHelpers/
 * 
 * Tweaked to work on WinPho. Changes include BitTools.ToLittleEndianString
 * and setting the duration properly based on bit rate & content length.
 * Also added support for non-seekable content and to be more memory-efficient
 * to avoid copying the entire audio stream's data into memory.
 */

namespace Media
{
    /// <summary>
    /// A Simple MediaStreamSource which can play back MP3 streams from
    /// beginning to end.
    /// </summary>
    public class Mp3MediaStreamSource : MediaStreamSource
    {
        /// <summary>
        ///  ID3 version 1 tags are 128 bytes at the end of the file.
        ///  http://www.id3.org/ID3v1
        /// </summary>
        private const int Id3Version1TagSize = 128;

        /// <summary>
        /// Buffer for decoding audio frames into.  4096 should be larger than we'll ever need, right? (144*448*1000/44100)
        /// </summary>
        private static readonly byte[] buffer = new byte[4096];

        /// <summary>
        /// The Mp3 stream being played back.
        /// </summary>
        private readonly Stream audioStream;

        /// <summary>
        /// The length of the audiostream as determined via the constructors.
        /// </summary>
        private readonly long audioStreamLength;

        /// <summary>
        /// Description of the Mp3 Stream being played back which includes the
        /// MpegLayer3WaveFormat structure serialized out as a string of hex 
        /// characters.
        /// </summary>
        private MediaStreamDescription audioStreamDescription;

        /// <summary>
        /// The current frame to parse
        /// </summary>
        private MpegFrame currentFrame;

        /// <summary>
        /// The position in the stream where the current MpegFrame starts.
        /// For purposes of this code, the frame starts with the header and
        /// not after the header.
        /// </summary>
        private long currentFrameStartPosition;

        /// <summary>
        /// Holds the duration of the track
        /// </summary>
        private TimeSpan trackDuration;
        public TimeSpan TrackDuration { get { return trackDuration; } }

        private long _headerPosition;

        private TimeSpan _Position;
        public TimeSpan Position
        {
            get { return _Position; }
            set
            {
                //if (currentFrame != null)
                //{
                //    currentFrameStartPosition = MpegFrame.FrameHeaderSize + (long)Math.Round(value.TotalSeconds / 8 * currentFrame.Bitrate);
                //    _Position = value;
                //}
            }
        }

        /// <summary>
        /// Initializes a new instance of the Mp3MediaStreamSource class.
        /// </summary>
        /// <param name="audioStream">
        /// Stream containing Mp3 data
        /// </param>
        public Mp3MediaStreamSource(Stream audioStream)
        {
            this.audioStream = audioStream;
            audioStreamLength = audioStream.Length;
        }

        /// <summary>
        /// Initializes a new instance of the Mp3MediaStreamSource class with a pre-determined length.
        /// This is useful for wrapping an IO stream that may not be seekable (and thus won't have a .Length)
        /// but for which you already know the length (e.g. a CryptoStream from an IsolatedStorageFileStream with a byte count,
        /// or an HTTP stream which has a specified content-length)
        /// </summary>
        /// <param name="audioStream">Non-seekable Stream containing Mp3 data</param>
        /// <param name="length">Length of non-seekable stream</param>
        public Mp3MediaStreamSource(Stream audioStream, long length)
        {
            this.audioStream = audioStream;
            audioStreamLength = length;
        }

        /// <summary>
        /// Gets the MpegLayer3WaveFormat structure which represents this Mp3 file.
        /// </summary>
        public MpegLayer3WaveFormat MpegLayer3WaveFormat { get; private set; }

        /// <summary>
        /// Read off the Id3Data from the stream and return the first MpegFrame of the audio stream.
        /// This assumes that the first bit of data is either an ID3 segment or an MPEG segment. Calls a separate thread
        /// to read past ID3v2 data.
        /// </summary>
        /// <param name="callback">
        /// Callback that can process the first MpegFrame and set up the MSS. Called back once the thread has skipped
        /// over all of the Id3V2 tags.
        /// </param>
        public void ReadPastId3V2Tags(Action<MpegFrame> callback)
        {
            Debug.WriteLine("ReadPastId3V2Tags");
            /* 
             * Since this code assumes that the first bit of data is either an ID3 segment or an MPEG segment it could
             * get into trouble. Should probably do something a bit more robust at some point.
             */

            MpegFrame mpegFrame;

            // Read and (throw out) any Id3 data if present. 
            var data = new byte[10];
            if (audioStream.Read(data, 0, 3) != 3)
            {
                goto cleanup;
            }

            if (data[0] == 73 /* I */&&
                data[1] == 68 /* D */&&
                data[2] == 51 /* 3 */)
            {
                // Need to update to read the is footer present flag and account for its 10 bytes if needed.
                if (audioStream.Read(data, 3, 7) != 7)
                {
                    goto cleanup;
                }

                int id3Size = BitTools.ConvertSyncSafeToInt32(data, 6);
                int bytesRead = 0;

                ThreadPool.QueueUserWorkItem(state =>
                                                 {
                                                     // Read through the ID3 Data tossing it out.)
                                                     while (id3Size > 0)
                                                     {
                                                         bytesRead = (id3Size - buffer.Length > 0)
                                                                         ? audioStream.Read(buffer, 0, buffer.Length)
                                                                         : audioStream.Read(buffer, 0, id3Size);
                                                         id3Size -= bytesRead;
                                                     }

                                                     mpegFrame = new MpegFrame(audioStream);
                                                     callback(mpegFrame);
                                                 });
            }
            else
            {
                // No ID3 tag present, presumably this is streaming and we are starting right at the Mp3 data.
                // Assume the stream isn't seekable.
                if (audioStream.Read(data, 3, 1) != 1)
                {
                    goto cleanup;
                }

                mpegFrame = new MpegFrame(audioStream, data);
                callback(mpegFrame);
            }

            return;

            // Cleanup and quit if you couldn't even read the initial data for some reason.
            cleanup:
            throw new Exception("Could not read intial audio stream data");
        }

        

        /// <summary>
        /// Parses the passed in MediaStream to find the first frame and signals
        /// to its parent MediaElement that it is ready to begin playback by calling
        /// ReportOpenMediaCompleted.
        /// </summary>
        protected override void OpenMediaAsync()
        {
            Debug.WriteLine("OpenMediaAsync");

            // Initialize data structures to pass to the Media pipeline via the MediaStreamSource
            var mediaSourceAttributes = new Dictionary<MediaSourceAttributesKeys, string>();
            var mediaStreamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
            var mediaStreamDescriptions = new List<MediaStreamDescription>();

            ReadPastId3V2Tags(
                mpegLayer3Frame =>
                ReadPastId3v2TagsCallback(mpegLayer3Frame, mediaStreamAttributes, mediaStreamDescriptions,
                                          mediaSourceAttributes));
        }

        /// <summary>
        /// Parses the next sample from the requested stream and then calls ReportGetSampleCompleted
        /// to inform its parent MediaElement of the next sample.
        /// </summary>
        /// <param name="mediaStreamType">
        /// Should always be Audio for this MediaStreamSource.
        /// </param>
        protected override void GetSampleAsync(MediaStreamType mediaStreamType)
        {
            Debug.WriteLine("Enter GetSampleAsync");

            var emptyDict = new Dictionary<MediaSampleAttributeKeys, string>();
            MediaStreamSample audioSample;

            if (currentFrame != null)
            {
                // Calculate our current position based on the stream's length
                double ratio = currentFrameStartPosition/(double) audioStreamLength;
                var currentPosition1 = new TimeSpan((long) (trackDuration.Ticks*ratio));

                // Calculate our current position instead based on the bitrate of the stream (more accurate?)
                double position = currentFrameStartPosition/(double) currentFrame.Bitrate;
                TimeSpan currentPosition = TimeSpan.FromSeconds(position*8 /* bits per Byte */);
                _Position = currentPosition;

                // Create a MemoryStream to hold the bytes
                // FrameSize includes the frame header which we've already read from the previous iteration, so just copy the
                // header, and then read the remaining bytes
                currentFrame.CopyHeader(buffer);
                int audioSampleSize = currentFrame.FrameSize - MpegFrame.FrameHeaderSize;
                //audioStream.Seek(_headerPosition +  currentFrameStartPosition - 4, SeekOrigin.Begin);
                int c = audioStream.Read(buffer, MpegFrame.FrameHeaderSize, audioSampleSize);
                if (c != audioSampleSize)
                {
                    // Ran out of bytes trying to read MP3 frame.
                    currentFrame = null;
                    audioSample = new MediaStreamSample(audioStreamDescription, null, 0, 0, 0, emptyDict);
                    ReportGetSampleCompleted(audioSample);
                    return;
                }

                currentFrameStartPosition += c;
                using (var audioFrameStream = new MemoryStream(buffer))
                {
                    // Return the next sample in the stream
                    audioSample = new MediaStreamSample(audioStreamDescription, audioFrameStream, 0,
                                                        currentFrame.FrameSize, currentPosition.Ticks, emptyDict);
                    ReportGetSampleCompleted(audioSample);

                    // Grab the next frame
                    var nextFrame = new MpegFrame(audioStream);
                    if ((nextFrame.Version == 1 || nextFrame.Version == 2) && nextFrame.Layer == 3)
                    {
                        currentFrameStartPosition += MpegFrame.FrameHeaderSize;
                        currentFrame = nextFrame;
                    }
                    else
                    {
                        currentFrame = null;
                    }
                }
            }
            else
            {
                // We're near the end of the file, or we got an irrecoverable error.
                // Return a null stream which tells the MediaStreamSource & MediaElement to shut down
                audioSample = new MediaStreamSample(audioStreamDescription, null, 0, 0, 0, emptyDict);
                ReportGetSampleCompleted(audioSample);
            }

            Debug.WriteLine("Exit GetSampleAsync");
        }

        /// <summary>
        ///  TODO FILL ME IN LATER
        /// </summary>
        protected override void CloseMedia()
        {
            try
            {
                audioStream.Close();
            }
            catch (CryptographicException)
            {
                // Ignore these, they are thrown when abruptly closing a
                // stream (i.e. skipping tracks) where the source is a
                // CryptoStream
            }
            catch (Exception e)
            {
                Debug.Assert(false, e.StackTrace);
            }
        }

        /// <summary>
        ///  TODO FILL ME IN LATER
        /// </summary>
        /// <param name="diagnosticKind">
        ///  TODO FILL ME IN LATER . . .
        /// </param>
        protected override void GetDiagnosticAsync(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <para>
        /// Effectively a Null-Op for when a MediaElement requests a seek at the beginning
        /// of the stream. This makes the stream semi-unseekable.
        /// </para>
        /// <para>
        /// In a fuller MediaStreamSource, the logic here would be to actually seek to
        /// the correct mpeg frame matching the seekToTime passed in.
        /// </para>
        /// </summary>
        /// <param name="seekToTime">
        ///  The time to seek to in nanosecond ticks.
        /// </param>
        protected override void SeekAsync(long seekToTime)
        {
            ReportSeekCompleted(seekToTime);
        }

        /// <summary>
        ///  TODO FILL ME IN LATER
        /// </summary>
        /// <param name="mediaStreamDescription">
        ///  TODO FILL ME IN LATER . . .
        /// </param>
        protected override void SwitchMediaStreamAsync(MediaStreamDescription mediaStreamDescription)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Callback which handles setting up an MSS once the first MpegFrame after Id3v2 data has been read.
        /// </summary>
        /// <param name="mpegLayer3Frame"> First MpegFrame</param>
        /// <param name="mediaStreamAttributes">Empty dictionary for MediaStreamAttributes</param>
        /// <param name="mediaStreamDescriptions">Empty dictionary for MediaStreamDescriptions</param>
        /// <param name="mediaSourceAttributes">Empty dictionary for MediaSourceAttributes</param>
        private void ReadPastId3v2TagsCallback(
            MpegFrame mpegLayer3Frame,
            Dictionary<MediaStreamAttributeKeys, string> mediaStreamAttributes,
            List<MediaStreamDescription> mediaStreamDescriptions,
            Dictionary<MediaSourceAttributesKeys, string> mediaSourceAttributes)
        {
            _headerPosition = audioStream.Position;

            Debug.WriteLine("ReadPastId3v2TagsCallback");

            if (mpegLayer3Frame.FrameSize <= 0)
            {
                throw new InvalidOperationException("MpegFrame's FrameSize cannot be negative");
            }

            // Initialize the Mp3 data structures used by the Media pipeline with state from the first frame.
            var wfx = new WaveFormatExtensible
                          {
                              FormatTag = 85,
                              Channels = (short) ((mpegLayer3Frame.Channels == Channel.SingleChannel) ? 1 : 2),
                              SamplesPerSec = mpegLayer3Frame.SamplingRate,
                              AverageBytesPerSecond = mpegLayer3Frame.Bitrate/8,
                              BlockAlign = 1,
                              BitsPerSample = 0,
                              ExtraDataSize = 12
                          };

            MpegLayer3WaveFormat = new MpegLayer3WaveFormat
                                       {
                                           WaveFormatExtensible = wfx,
                                           Id = 1,
                                           BitratePaddingMode = 0,
                                           FramesPerBlock = 1,
                                           BlockSize = (short) mpegLayer3Frame.FrameSize,
                                           CodecDelay = 0
                                       };


            mediaStreamAttributes[MediaStreamAttributeKeys.CodecPrivateData] = MpegLayer3WaveFormat.ToHexString();
            audioStreamDescription = new MediaStreamDescription(MediaStreamType.Audio, mediaStreamAttributes);

            mediaStreamDescriptions.Add(audioStreamDescription);
            trackDuration = new TimeSpan(0, 0,
                                         (int)
                                         (audioStreamLength/
                                          MpegLayer3WaveFormat.WaveFormatExtensible.AverageBytesPerSecond));
            mediaSourceAttributes[MediaSourceAttributesKeys.Duration] = trackDuration.Ticks.ToString(CultureInfo.InvariantCulture);

            if (audioStream.CanSeek)
            {
                mediaSourceAttributes[MediaSourceAttributesKeys.CanSeek] = "1";
            }
            else
            {
                mediaSourceAttributes[MediaSourceAttributesKeys.CanSeek] = "0";
            }

            // Report that the Mp3MediaStreamSource has finished initializing its internal state and can now
            // pass in Mp3 Samples.
            ReportOpenMediaCompleted(mediaSourceAttributes, mediaStreamDescriptions);

            currentFrame = mpegLayer3Frame;
            currentFrameStartPosition = MpegFrame.FrameHeaderSize;
        }
    }
}