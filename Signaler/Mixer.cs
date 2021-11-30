using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Timers;

namespace Signaler
{
    public class Mixer
    {
        private readonly ILogger<Mixer> _logger;

        private WaveOutEvent _waveOutEvent;
        private BufferedWaveProvider _waveProvider;

        public Mixer()
        {

        }
    }
}
