namespace StreamExtract.Plugins;

internal static class MkvCodecExtensions
{
    public static string GetExtension(string codecId) => codecId switch
    {
        "A_AAC/MPEG2/*" or "A_AAC/MPEG4/*" or "A_AAC" => "aac",
        "A_AC3" or "A_EAC3" => "ac3", "A_ALAC" => "caf", "A_DTS" => "dts",
        "A_FLAC" => "flac", "A_MPEG/L2" => "mp2", "A_MPEG/L3" => "mp3",
        "A_OPUS" => "opus", "A_PCM/INT/LIT" or "A_PCM/INT/BIG" => "wav",
        "A_REAL/*" => "ra", "A_TRUEHD" or "A_MLP" => "mlp", "A_TTA1" => "tta",
        "A_VORBIS" => "ogg", "A_WAVPACK4" => "wv", "S_HDMV/PGS" => "sup",
        "S_HDMV/TEXTST" => "textst", "S_KATE" => "ogg",
        "S_TEXT/SSA" or "S_SSA" => "ssa", "S_TEXT/ASS" or "S_ASS" => "ass",
        "S_TEXT/UTF8" or "S_TEXT/ASCII" => "srt", "S_VOBSUB" => "sub",
        "S_TEXT/USF" => "usf", "S_TEXT/WEBVTT" or "D_WEBVTT/SUBTITLES" => "vtt",
        "V_MPEG1" or "V_MPEG2" => "mpeg", "V_MPEG4/ISO/AVC" => "h264",
        "V_MPEG4/ISO/HEVC" or "V_MPEGH/ISO/HEVC" => "h265",
        "V_MS/VFW/FOURCC" => "avi", "V_REAL/*" => "rm", "V_THEORA" => "ogg",
        "V_VP8" or "V_VP9" => "ivf",
        _ => "bin"
    };
}
