//-----------------------------------------------------------------------
// <copyright file="WaveMediaStreamSource.cs" company="Gilles Khouzam">
// (c) Copyright Gilles Khouzam
// This source is subject to the Microsoft Public License (Ms-PL)
// All other rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace WaveMSS
{
    /// <summary>
    /// A Media Stream Source implemented to play WAVE files
    /// </summary>
    public class WaveMediaStreamSource : MediaStreamSource, IDisposable
    {
        /// <summary>
        /// The sample attributes (not used so empty)
        /// </summary>
        private readonly Dictionary<MediaSampleAttributeKeys, string> _emptySampleDict =
            new Dictionary<MediaSampleAttributeKeys, string>();

        /// <summary>
        /// The stream that we're playing back
        /// </summary>
        private readonly Stream _stream;

        /// <summary>
        /// The stream description
        /// </summary>
        private MediaStreamDescription _audioDesc;

        /// <summary>
        /// The current position in the stream.
        /// </summary>
        private long _currentPosition;

        /// <summary>
        /// The current timestamp
        /// </summary>
        private long _currentTimeStamp;

        /// <summary>
        /// The start position of the data in the stream
        /// </summary>
        private long _startPosition;

        /// <summary>
        /// The WavParser that can extract the data
        /// </summary>
        private WavParser _wavParser;

        public double DurationMs
        {
            get
            {
                if (_wavParser == null)
                    throw  new Exception("You can get the media duration only after the media is loaded");

                return _wavParser.Duration / 10000.0;
            }
        }

        public bool LoopForever { get; set; }

        /// <summary>
        /// Initializes a new instance of the WaveMediaStreamSource class.
        /// </summary>
        /// <param name="stream">The stream the will contain the data to playback</param>
        public WaveMediaStreamSource(Stream stream)
        {
            this._stream = stream;
        }

        #region IDisposable Members

        /// <summary>
        /// Implement the Dispose method to release the resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        /// <summary>
        /// Implementation of the IDisposable pattern
        /// </summary>
        /// <param name="disposing">Are we being destroyed</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_wavParser != null)
                {
                    _wavParser.Dispose();
                    _wavParser = null;
                }
            }
        }

        /// <summary>
        /// Open the media.
        /// Create the structures.
        /// </summary>
        protected override void OpenMediaAsync()
        {
            // Create a parser
            _wavParser = new WavParser(_stream);

            // Parse the header
            _wavParser.ParseWaveHeader();

            _wavParser.WaveFormatEx.ValidateWaveFormat();

            _startPosition = _currentPosition = _wavParser.DataPosition;

            // Init
            var streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
            var sourceAttributes = new Dictionary<MediaSourceAttributesKeys, string>();
            var availableStreams = new List<MediaStreamDescription>();

            // Stream Description
            streamAttributes[MediaStreamAttributeKeys.CodecPrivateData] = _wavParser.WaveFormatEx.ToHexString();
            var msd = new MediaStreamDescription(MediaStreamType.Audio, streamAttributes);

            _audioDesc = msd;
            availableStreams.Add(_audioDesc);

            sourceAttributes[MediaSourceAttributesKeys.Duration] = _wavParser.Duration.ToString();
            ReportOpenMediaCompleted(sourceAttributes, availableStreams);
        }

        /// <summary>
        /// Close the media. Release the resources.
        /// </summary>
        protected override void CloseMedia()
        {
            // Close the stream
            _startPosition = _currentPosition = 0;
            _wavParser = null;
            _audioDesc = null;
        }

        /// <summary>
        /// Not implemented
        /// </summary>
        /// <param name="diagnosticKind">The diagnostic kind</param>
        protected override void GetDiagnosticAsync(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Return the next sample requested
        /// </summary>
        /// <param name="mediaStreamType">The stream type that we are getting a sample for</param>
        protected override void GetSampleAsync(MediaStreamType mediaStreamType)
        {
            //
            // Summary:
            //     Developers call this method in response to System.Windows.Media.MediaStreamSource.GetSampleAsync(System.Windows.Media.MediaStreamType)
            //     to give the System.Windows.Controls.MediaElement the next media sample to
            //     be rendered, or to report the end of a stream.
            //
            //ReportGetSampleProgress();

            // Start with one second of data, rounded up to the nearest block.
            var bufferSize = (uint) AlignUp(
                _wavParser.WaveFormatEx.AvgBytesPerSec,
                _wavParser.WaveFormatEx.BlockAlign);

            // Figure out how much data we have left in the chunk compared to the
            // data that we need.
            bufferSize = Math.Min(bufferSize, _wavParser.BytesRemainingInChunk);
            if (bufferSize > 0)
            {
                _wavParser.ProcessDataFromChunk(bufferSize);

                // Send out the next sample
                var sample = new MediaStreamSample(
                    _audioDesc,
                    _stream,
                    _currentPosition,
                    bufferSize,
                    _currentTimeStamp,
                    _emptySampleDict);

                // Move our timestamp and position forward
                _currentTimeStamp += _wavParser.WaveFormatEx.AudioDurationFromBufferSize(bufferSize);
                _currentPosition += bufferSize;

                if (LoopForever)
                {
                    // If there are no more bytes in the chunk, start again from the beginning
                    if (_wavParser.BytesRemainingInChunk == 0)
                    {
                        _wavParser.MoveToStartOfChunk();
                        _currentPosition = _startPosition;
                    }
                }

                ReportGetSampleCompleted(sample);
                FireMediaPositionChanged();
            }
            else
            {
                // Report EOS
                ReportGetSampleCompleted(new MediaStreamSample(_audioDesc, null, 0, 0, 0, _emptySampleDict));
                FireMediaEnded();
            }
        }

        public event Action OnMediaEnded;
        public event Action<double> OnMediaPositionChanged;

        private void FireMediaEnded()
        {
            if (OnMediaEnded != null)
                OnMediaEnded();
        }

        private void FireMediaPositionChanged()
        {
            if (OnMediaPositionChanged != null)
                OnMediaPositionChanged(_currentTimeStamp/10000.0);
        }

        /// <summary>
        /// Called when asked to seek to a new position
        /// </summary>
        /// <param name="seekToTime">the time to seek to</param>
        protected override void SeekAsync(long seekToTime)
        {
            if (seekToTime > _wavParser.Duration)
            {
                throw new InvalidOperationException("The seek position is beyond the length of the stream");
            }

            _currentPosition = _wavParser.WaveFormatEx.BufferSizeFromAudioDuration(seekToTime) + _startPosition;
            _currentTimeStamp = seekToTime;

            _wavParser.MoveToChunkOffset((uint)_currentPosition);

            ReportSeekCompleted(seekToTime);
        }

        /// <summary>
        /// Stream media stream.
        /// Not implemented
        /// </summary>
        /// <param name="mediaStreamDescription">The mediaStreamDescription that we want to switch to</param>
        protected override void SwitchMediaStreamAsync(MediaStreamDescription mediaStreamDescription)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Helper function to align a block
        /// </summary>
        /// <param name="a">The value we want to align</param>
        /// <param name="b">The alignment value</param>
        /// <returns>A new aligned value</returns>
        private static int AlignUp(int a, int b)
        {
            int tmp = a + b - 1;
            return tmp - (tmp%b);
        }
    }
}