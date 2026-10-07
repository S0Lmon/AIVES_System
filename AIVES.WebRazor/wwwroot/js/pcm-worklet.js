// Runs on the audio thread: hands each block of microphone samples to the page,
// which downsamples to 16 kHz and streams it to the server.
class PcmCapture extends AudioWorkletProcessor {
    process(inputs) {
        const channel = inputs[0] && inputs[0][0];
        if (channel && channel.length) this.port.postMessage(channel.slice(0));
        return true;
    }
}
registerProcessor('pcm-capture', PcmCapture);
