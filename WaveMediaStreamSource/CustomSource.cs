using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace WaveMSS
{
    public class CustomSource : MediaStreamSource, IDisposable
    {
        private const int _frameWidth = 320, _frameHeight = 200;
        private const int _framePixelSize = 4;
        private const int _frameBufferSize = _frameHeight*_frameWidth*_framePixelSize;
        private const int _frameStreamSize = _frameBufferSize*100;
        private MemoryStream _frameStream = new MemoryStream(_frameStreamSize);
        private int _frameTime;
        private MediaStreamDescription _videoDesc;

        #region IDisposable Members

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {

            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion


        private int _frameStreamOffset = 0;
        private Dictionary<MediaSampleAttributeKeys, string> _emptySampleDict =
            new Dictionary<MediaSampleAttributeKeys, string>();
        private Random _random = new Random();
        private byte[] _frameBuffer = new byte[_frameBufferSize];
        private long _currentTime;

        private void GetVideoSample()
        {
            if (_frameStreamOffset + _frameBufferSize > _frameStreamSize)
            {
                _frameStream.Seek(0, SeekOrigin.Begin);
                _frameStreamOffset = 0;
            }
            //for (int i = 0; i < _frameBufferSize; i += _framePixelSize)
            //{
            //    if (_random.Next(0, 2) > 0)
            //    {
            //        _frameBuffer[i] = _frameBuffer[i + 1] =
            //                          _frameBuffer[i + 2] = 0x55;
            //    }
            //    else
            //    {
            //        _frameBuffer[i] = _frameBuffer[i + 1] =
            //                          _frameBuffer[i + 2] = 0xDD;
            //    }
            //    _frameBuffer[i + 3] = 0xFF;
            //}
            for (int i = 0; i < _frameBufferSize; i += _framePixelSize)
            {
                //_frameBuffer[i] = 60;
                //_frameBuffer[i + 1] = 20;
                //_frameBuffer[i + 2] = 96;
                _frameBuffer[i + 3] = 0xFF;
            }
            //DrawLine(0, _random.Next(_frameWidth), 0, _random.Next(_frameHeight), _frameBuffer);
            DrawLine(0, _frameWidth, 0, _frameHeight, _frameBuffer);
            DrawLine(0, _frameWidth - 1, 1, _frameHeight, _frameBuffer);
            DrawLine(1, _frameWidth, 0, _frameHeight - 1, _frameBuffer);

            _frameStream.Write(_frameBuffer, 0, _frameBufferSize);
            var msSamp = new MediaStreamSample(_videoDesc, _frameStream, _frameStreamOffset, _frameBufferSize,
                                               _currentTime, _emptySampleDict);
            _currentTime += _frameTime;
            _frameStreamOffset += _frameBufferSize;
            ReportGetSampleCompleted(msSamp);
        }

        private void DrawLine(int x1, int x2, int y1, int y2, byte[] bytes)
        {
            Color color = Colors.Orange;

            double a = (x2 == x1) ? (y2 - y1) : (double)(y1*x2 - y2*x1)/(x2 - x1);
            double b = (y1 == y2) ? 0 : ((x1 == x2) ? 0 : (double)(y2 - y1)/(x2 - x1));

            for (int x = x1; x < x2; x++)
            {
                double y_d = a + b*x;
                int y = (int) Math.Round(y_d);

                int x_pixel = y*_framePixelSize*_frameWidth + x*_framePixelSize;

                bytes[x_pixel] =color.R;
                bytes[x_pixel + 1] = color.G;
                bytes[x_pixel + 2] = color.B;
            }
        }

        private void PrepareVideo()
        {
            _frameTime = (int) TimeSpan.FromSeconds((double) 1/30).Ticks;
            var streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
            streamAttributes[MediaStreamAttributeKeys.VideoFourCC] = "RGBA";
            streamAttributes[MediaStreamAttributeKeys.Height] = _frameHeight.ToString();
            streamAttributes[MediaStreamAttributeKeys.Width] = _frameWidth.ToString();
            _videoDesc = new MediaStreamDescription(MediaStreamType.Video, streamAttributes);
        }

        protected override void OpenMediaAsync()
        {
            var sourceAttributes = new Dictionary<MediaSourceAttributesKeys, string>();
            var availableStreams = new List<MediaStreamDescription>();
            PrepareVideo();
            availableStreams.Add(_videoDesc);
            sourceAttributes[MediaSourceAttributesKeys.Duration] =
                TimeSpan.FromSeconds(0).Ticks.ToString(CultureInfo.InvariantCulture);
            sourceAttributes[MediaSourceAttributesKeys.CanSeek] = false.ToString();
            ReportOpenMediaCompleted(sourceAttributes, availableStreams);
        }

        protected override void SeekAsync(long seekToTime)
        {
            _currentTime = seekToTime;
            ReportSeekCompleted(seekToTime);
        }

        protected override void GetSampleAsync(MediaStreamType mediaStreamType)
        {
            switch (mediaStreamType)
            {
                case MediaStreamType.Audio:
                    GetAudioSample();
                    break;
                case MediaStreamType.Video:
                    GetVideoSample();
                    break;
            }
        }

        private void GetAudioSample()
        {
           
        }

        protected override void SwitchMediaStreamAsync(MediaStreamDescription mediaStreamDescription)
        {
            throw new NotImplementedException();
        }

        protected override void GetDiagnosticAsync(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            throw new NotImplementedException();
        }

        protected override void CloseMedia()
        {

        }
    }
}