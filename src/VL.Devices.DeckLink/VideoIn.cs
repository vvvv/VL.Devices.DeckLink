using DeckLinkAPI;
using Stride.Core;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Storage;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Images;
using Stride.Shaders;
using Stride.Shaders.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VL.Devices.DeckLink
{
    public enum Colorspace
    {
        BT601,
        BT709,
        BT601_SRGB
    }

    public class VideoCaptureRenderer : RendererBase, IDeckLinkInputCallback
    {
        readonly SerialDisposable deviceSubscription = new SerialDisposable();

        public DeviceEnumEntry Device
        {
            set
            {
                if (value?.Value != deviceName)
                {
                    deviceName = value?.Value;

                    var iterator = new CDeckLinkIterator();
                    var inputDevice = default(IDeckLinkInput);
                    while (true)
                    {
                        iterator.Next(out var deckLink);
                        if (deckLink is null)
                            break;

                        if (deckLink is IDeckLinkInput deckLinkInput)
                        {
                            deckLink.GetModelName(out var modelName);
                            deckLink.GetDisplayName(out var displayName);
                            if (displayName == deviceName)
                            {
                                inputDevice = deckLinkInput;
                                break;
                            }
                        }
                    }

                    InputDevice = inputDevice;
                }
            }
        }
        string deviceName;

        public IDeckLinkInput InputDevice
        {
            get => inputDevice;
            set
            {
                if (value != inputDevice)
                {
                    inputDevice = value;
                    Resubscribe();
                }
            }
        }
        IDeckLinkInput inputDevice;

        /// <summary>
        /// Preferred display mode if manual
        /// </summary>
        public _BMDDisplayMode PreferredDisplayMode
        {
            get => preferredDisplayMode;
            set
            {
                if (value != preferredDisplayMode)
                {
                    preferredDisplayMode = value;
                    Resubscribe();
                }
            }
        }
        _BMDDisplayMode preferredDisplayMode = _BMDDisplayMode.bmdModeHD1080p6000;

        public _BMDPixelFormat PreferredPixelFormat
        {
            get => preferredPixelFormat;
            set
            {
                if (value != preferredPixelFormat)
                {
                    preferredPixelFormat = value;
                    Resubscribe();
                }
            }
        }
        _BMDPixelFormat preferredPixelFormat = _BMDPixelFormat.bmdFormat8BitYUV;

        public bool ApplyDetectedDisplayMode
        {
            get => applyDetectedDisplayMode;
            set
            {
                if (value != applyDetectedDisplayMode)
                {
                    applyDetectedDisplayMode = value;
                    Resubscribe();
                }
            }
        }
        bool applyDetectedDisplayMode;

        public Texture CurrentVideoFrame
        {
            get => currentVideoFrame;
            private set
            {
                if (value != currentVideoFrame)
                {
                    currentVideoFrame?.Dispose();
                    currentVideoFrame = value;
                }
            }
        }
        Texture currentVideoFrame;

        public Colorspace Colorspace
        {
            get => conversion;
            set
            {
                if (value != conversion)
                {
                    conversion = value;
                    shader?.Dispose();
                    shader = null;
                    isShaderInitialized = false;
                }
            }
        }
        Colorspace conversion;

        public bool IsStreaming => currentDisplayMode != _BMDDisplayMode.bmdModeUnknown;

        void Resubscribe()
        {
            deviceSubscription.Disposable = null;
            deviceSubscription.Disposable = Subscribe(inputDevice, preferredDisplayMode, preferredPixelFormat);
        }

        IDisposable Subscribe(IDeckLinkInput inputDevice, _BMDDisplayMode requestedDisplayMode, _BMDPixelFormat requestedPixelFormat)
        {
            if (inputDevice is null)
                return Disposable.Empty;

            try
            {
                inputDevice.DoesSupportVideoMode(
                    _BMDVideoConnection.bmdVideoConnectionUnspecified,
                    requestedDisplayMode,
                    requestedPixelFormat,
                    _BMDSupportedVideoModeFlags.bmdSupportedVideoModeDefault,
                    out var supported);

                if (supported == 0)
                {
                    requestedDisplayMode = preferredDisplayMode;
                    requestedPixelFormat = preferredPixelFormat;
                }

                inputDevice.EnableVideoInput(
                    requestedDisplayMode,
                    requestedPixelFormat,
                    applyDetectedDisplayMode ? _BMDVideoInputFlags.bmdVideoInputEnableFormatDetection : _BMDVideoInputFlags.bmdVideoInputFlagDefault);

                inputDevice.SetCallback(this);

                inputDevice.StartStreams();

                currentDisplayMode = requestedDisplayMode;
                currentPixelFormat = requestedPixelFormat;

                return Disposable.Create(() =>
                {
                    currentDisplayMode = _BMDDisplayMode.bmdModeUnknown;
                    inputDevice.StopStreams();
                    inputDevice.FlushStreams();
                    inputDevice.DisableVideoInput();
                    inputDevice.SetCallback(null);
                });
            }
            catch (Exception)
            {
                currentDisplayMode = _BMDDisplayMode.bmdModeUnknown;
                return Disposable.Empty;
            }
        }
        _BMDDisplayMode currentDisplayMode = _BMDDisplayMode.bmdModeUnknown;
        _BMDPixelFormat currentPixelFormat = _BMDPixelFormat.bmdFormatUnspecified;

        void IDeckLinkInputCallback.VideoInputFormatChanged(_BMDVideoInputFormatChangedEvents notificationEvents, IDeckLinkDisplayMode newDisplayMode, _BMDDetectedVideoInputFormatFlags detectedSignalFlags)
        {
            // Restart capture with the new video mode if told to
            if (!applyDetectedDisplayMode)
                return;

            // Resubscribe with new display mode
            deviceSubscription.Disposable = null;
            Subscribe(inputDevice, newDisplayMode.GetDisplayMode(), preferredPixelFormat);
        }

        private readonly RingBuffer<Texture> ringBuffer = new RingBuffer<Texture>(2);
        private readonly object ringBufferLock = new object();
        private readonly ManualResetEventSlim videoFrameArrived = new ManualResetEventSlim();

        //CDeckLinkVideoConversion c = new CDeckLinkVideoConversion();
        //IDeckLinkMutableVideoFrame convertedFrame;
        //if (this.convertedFrame != null)
        //{
        //    Marshal.ReleaseComObject(this.convertedFrame);
        //}
        //var device = inputDevice as IDeckLinkOutput;
        //device?.CreateVideoFrame(width, height, width * 4, _BMDPixelFormat.bmdFormat8BitBGRA, _BMDFrameFlags.bmdFrameFlagDefault, out this.convertedFrame);
        //c.ConvertFrame(videoFrame, convertedFrame);

        unsafe void IDeckLinkInputCallback.VideoInputFrameArrived(IDeckLinkVideoInputFrame videoFrame, IDeckLinkAudioInputPacket audioPacket)
        {
            if (videoFrame is null)
                return;

            if (Services is null)
                return;

            var width = videoFrame.GetWidth();
            var height = videoFrame.GetHeight();

            if (needsConversion)
                width = width / 2;

            videoFrame.GetBytes(out var ptr);
            var data = new DataBox(ptr, videoFrame.GetRowBytes(), videoFrame.GetRowBytes() * height);

            var x = new Span<ColorBGRA>(ptr.ToPointer(), videoFrame.GetRowBytes() * height / sizeof(ColorBGRA));

            var renderContext = RenderContext.GetShared(Services);
            var texture = Texture.New2D(
                renderContext.GraphicsDevice,
                width,
                height,
                mipCount: 1,
                format: PixelFormat.B8G8R8A8_UNorm /*videoFrame.GetPixelFormat().ToPixelFormat()*/,
                textureData: new[] { data },
                usage: GraphicsResourceUsage.Immutable);

            Marshal.ReleaseComObject(videoFrame);

            lock (ringBufferLock)
            {
                // In case the buffer is full the element at the front will be popped.
                if (ringBuffer.IsFull)
                {
                    ringBuffer.Front().Dispose();
                }

                // Place the texture in the buffer
                ringBuffer.PushBack(texture);
            }

            // Signal render thread that a new frame is available
            videoFrameArrived.Set();
        }

        public Texture Update()
        {
            // Check if deck link device is available and we're streaming
            if (inputDevice is null || !IsStreaming)
                return null;

            // Fetch the texture
            FetchCurrentVideoFrame();

            if (currentPixelFormat == _BMDPixelFormat.bmdFormat8BitYUV)
            {
                needsConversion = true;
                return current.outputTexture;
            }
            else
            {
                needsConversion = false;
                return currentVideoFrame;
            }
        }
        private bool needsConversion;

        void FetchCurrentVideoFrame()
        {
            // Fetch the texture
            lock (ringBufferLock)
            {
                if (!ringBuffer.IsEmpty)
                {
                    // Set the texture as current output
                    var texture = ringBuffer.Front();
                    ringBuffer.PopFront();
                    CurrentVideoFrame = texture;
                    return;
                }
            }

            // The buffer was empty, wait for the next video frame to arrive
            inputDevice.GetDisplayMode(currentDisplayMode, out var displayMode);
            displayMode.GetFrameRate(out var frameDuration, out var timeScale);
            var fps = ((double)timeScale) / frameDuration;
            var frameTime = 1d / fps;
            var x = ((double)frameDuration) / timeScale;
            //var waitTime = TimeSpan.FromTicks(frameDuration);
            var waitTime = TimeSpan.FromSeconds(1);
            if (videoFrameArrived.Wait(waitTime))
            {
                // Reset the wait handle
                videoFrameArrived.Reset();

                lock (ringBufferLock)
                {
                    if (!ringBuffer.IsEmpty)
                    {
                        // Set the texture as current output
                        var texture = ringBuffer.Front();
                        ringBuffer.PopFront();
                        CurrentVideoFrame = texture;
                        return;
                    }
                }
            }
        }

        public PixelFormat OutputTextureFormat { get; set; } = PixelFormat.B8G8R8A8_UNorm_SRgb;

        protected override void DrawCore(RenderDrawContext context)
        {
            if (CurrentVideoFrame is null || !needsConversion)
                return;

            // Prepare render target
            if (CurrentVideoFrame.Description != current.inputDesc || current.outputTexture.Format != OutputTextureFormat)
            {
                current.outputTexture?.Dispose();
                current.inputDesc = CurrentVideoFrame.Description;

                var outputDesc = TextureDescription.FromDescription(
                    current.inputDesc,
                    TextureFlags.RenderTarget | TextureFlags.ShaderResource, GraphicsResourceUsage.Default);

                // YUV is compressed
                outputDesc.Width *= 2;
                outputDesc.Format = OutputTextureFormat;

                current.outputTexture = Texture.New(context.GraphicsDevice, outputDesc);
            }

            var shader = this.shader ?? (this.shader = CreateShader(context));
            shader.SetInput(CurrentVideoFrame);
            shader.SetOutput(current.outputTexture);
            shader.Draw(context);
        }
        (TextureDescription inputDesc, Texture outputTexture) current;

        ImageEffectShader CreateShader(RenderDrawContext context)
        {
            if (isShaderInitialized)
                return this.shader;

            isShaderInitialized = true;

            // Setup our own effect compiler
            var databaseFileProvider = context.RenderContext.Services.GetService<IDatabaseFileProviderService>();
            using var fileProvider = new InMemoryFileProvider(databaseFileProvider.FileProvider);
            fileProvider.Register("shaders/YUV2RGB.sdsl", GetShaderSource());
            using var compiler = new EffectCompiler(fileProvider);
            compiler.SourceDirectories.Add("shaders");

            // Switch effect compiler
            var currentCompiler = context.RenderContext.Effects.Compiler;
            context.RenderContext.Effects.Compiler = compiler;
            try
            {
                var shader = new ImageEffectShader("YUV2RGB");
                shader.SetInput(CurrentVideoFrame);
                shader.SetOutput(current.outputTexture);
                shader.Draw(context);
                return shader;
            }
            finally
            {
                context.RenderContext.Effects.Compiler = currentCompiler;
            }

            string GetShaderSource()
            {
                string s = default;
                switch (conversion)
                {
                    case Colorspace.BT601:
                        s = @"
// vvvv
// https://docs.microsoft.com/en-us/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering#converting-8-bit-yuv-to-rgb888
	    float4 col;
	    col.r = 1.164383 * c + 1.596027 * e;
	    col.g = 1.164383 * c - (0.391762 * d) - (0.812968 * e);
	    col.b = 1.164383 * c +  2.017232 * d;
	    col.a = 1.0f;
";
                        break;
                    case Colorspace.BT709:
                        s = @"
// https://forum.blackmagicdesign.com/viewtopic.php?f=12&t=29413 
	    float4 col;
	    col.r = 1.164383 * c + 1.792741 * e;
	    col.g = 1.164383 * c - (0.213249 * d) - (0.532909 * e);
	    col.b = 1.164383 * c +  2.112402 * d;
	    col.a = 1.0f;
";
                        break;
                    case Colorspace.BT601_SRGB:
                        s = @"
// vvvv
// https://docs.microsoft.com/en-us/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering#converting-8-bit-yuv-to-rgb888
	    float4 col;
	    col.r = pow(1.164383 * c + 1.596027 * e, 2.2);
	    col.g = pow(1.164383 * c - (0.391762 * d) - (0.812968 * e), 2.2);
	    col.b = pow(1.164383 * c +  2.017232 * d, 2.2);
	    col.a = 1.0f;
";
                        break;
                    default:
                        break;
                }
                return @"
shader YUV2RGB : ImageEffectShader
{
    stage override float4 Shading()
    {
	    uint pixel = streams.TexCoord.x / Texture0TexelSize.x;
	    bool rightPixel = pixel % 2 == 0;
	
        float4 uyvy = Texture0.Sample(PointSampler, streams.TexCoord);
	    float y1 = uyvy.a;
	    float y2 = uyvy.g;
	    float u = uyvy.b;
	    float v = uyvy.r;
	
	    float y = rightPixel ? y2 : y1;
	
	    float c = y - (16.0f / 256.0f);
	    float d = u - 0.5f;
	    float e = v - 0.5;
	
" + s + @"
	
        return col;
    }
};
";
            }
        }
        private bool isShaderInitialized;
        private ImageEffectShader shader;

        protected override void Destroy()
        {
            deviceSubscription.Dispose();

            current.outputTexture?.Dispose();

            lock (ringBufferLock)
            {
                foreach (var texture in ringBuffer)
                    texture?.Dispose();
            }

            base.Destroy();
        }

        class InMemoryFileProvider : VirtualFileProviderBase, IDisposable
        {
            readonly Dictionary<string, byte[]> inMemory = new Dictionary<string, byte[]>();
            readonly HashSet<string> tempFiles = new HashSet<string>();
            readonly IVirtualFileProvider virtualFileProvider;

            public InMemoryFileProvider(IVirtualFileProvider baseFileProvider) : base(baseFileProvider.RootPath)
            {
                virtualFileProvider = baseFileProvider;
            }

            public void Register(string url, string content)
            {
                inMemory[url] = Encoding.Default.GetBytes(content);

                // The effect system assumes there is a /path - doesn't do a FileExists check first :/
                var path = GetTempFileName();
                tempFiles.Add(path);
                File.WriteAllText(path, content);
                inMemory[$"{url}/path"] = Encoding.Default.GetBytes(path);
            }

            // Don't use Path.GetTempFileName() where we can run into overflow issue (had it during development)
            // https://stackoverflow.com/questions/18350699/system-io-ioexception-the-file-exists-when-using-system-io-path-gettempfilena
            private string GetTempFileName()
            {
                return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            }

            public new void Dispose()
            {
                try
                {
                    foreach (var f in tempFiles)
                        File.Delete(f);
                }
                catch (Exception)
                {
                    // Ignore
                }
                base.Dispose();
            }

            public override bool FileExists(string url)
            {
                if (inMemory.ContainsKey(url))
                    return true;

                return virtualFileProvider.FileExists(url);
            }

            public override Stream OpenStream(string url, VirtualFileMode mode, VirtualFileAccess access, VirtualFileShare share = VirtualFileShare.Read, StreamFlags streamFlags = StreamFlags.None)
            {
                if (inMemory.TryGetValue(url, out var bytes))
                {
                    return new MemoryStream(bytes);
                }

                return virtualFileProvider.OpenStream(url, mode, access, share, streamFlags);
            }
        }
    }
}
