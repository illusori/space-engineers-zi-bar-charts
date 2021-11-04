const string SCRIPT_FULL_NAME = "Zephyr Industries Bar Charts";
const string SCRIPT_SHORT_NAME = "ZI Bar Charts";
const string SCRIPT_VERSION = "3.1.0";
const string SCRIPT_ID = "ZIBarCharts";
const string PUBSUB_ID = "zi.bar-charts";

const int HISTORY     = 100;
const int SAMPLES     = 10;

// FIXME no need to be a list anymore
const int PANELS_CHART = 0;
const int SIZE_PANELS  = 1;

const int UPDATING_NONE     = 0;
const int UPDATING_CHARTS   = 1;
const int UPDATING_DISPLAYS = 2;

// FIXME???
const string CHART_DATAPOINTS = "Chart Data In";

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@ChartDisplay" };

/* Genuine global state */

readonly ZIScript _zis;

int _cycles = 0;

List<List<IMyTextSurfaceProvider>> _panels = new List<List<IMyTextSurfaceProvider>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "" };
List<IMyProgrammableBlock> _pubsub_blocks = new List<IMyProgrammableBlock>();

int _last_run_datapoints = 0;

int _updating = UPDATING_NONE;
bool _dirty = false;

/* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
// FIXME: _chart here? _panel?
MyCommandLine _command_line = new MyCommandLine();

public Program() {
    _zis = new ZIScript(this, mainloop_handler: this.MainLoop, send_data_events_to_self: true);
    //_zicd = new ZICompositeDisplays(this);

    Chart.Program = this;
    ChartDisplay.Program = this;
    DrawBuffer.Program = this;
    SlottedSprite.Program = this;

    // FIXME
    for (int i = 0; i < SIZE_PANELS; i++) {
	_panels.Add(new List<IMyTextSurfaceProvider>());
    }

    // Create load/time charts
    Chart.Create(CHART_DATAPOINTS, "");

    FindPanels();

    _zis.Subscribe("dataset.create",  this.EventDatasetCreate);
    _zis.AddCommand("create", this.CommandCreate);
    _zis.AddCommand("add",    this.CommandAdd);

    Runtime.UpdateFrequency |= UpdateFrequency.Update100 | UpdateFrequency.Update1;
}

public void Save() {
}

public void Main(string argument, UpdateType updateSource) {
    _zis.Main(argument, updateSource);
}

public void Log(string m) { _zis.Log(m); }
public void Warning(string m) { _zis.Warning(m); }

public void MainLoop(UpdateType updateSource) {
    // Run for Update1 but only if not also Update100.
    if ((updateSource & (UpdateType.Update1 | UpdateType.Update100)) == UpdateType.Update1) {
        if ((_updating == UPDATING_NONE) && _dirty) {
            _updating = UPDATING_CHARTS;
            MarkClean();
        }
        if (_updating == UPDATING_CHARTS) {
            if (!Chart.DrawNextChart()) {
                _updating = UPDATING_DISPLAYS;
            }
        } else if (_updating == UPDATING_DISPLAYS) {
            if (!Chart.FlushNextChartBuffer()) {
		if (_dirty) {
		    _updating = UPDATING_CHARTS;
		    MarkClean();
		} else {
		    _updating = UPDATING_NONE;
		}
            }
        }
    }
    if ((updateSource & UpdateType.Update100) != 0) {
	_cycles++;

	Chart.Find(CHART_DATAPOINTS).AddDatapoint((double)_last_run_datapoints);
	_last_run_datapoints = 0;

	if ((_cycles % 30) == 1) {
	    FindPanels(); // FIXME
	}

	// FIXME: alternate update and flush?
	// FIXME: update could update one chart per cycle
	// FIXME: flush could flush one drawbuffer per cycle
/*
        if ((_updating == UPDATING_NONE) && _dirty) {
            _updating = UPDATING_CHARTS;
            MarkClean();
        }
        if (_updating == UPDATING_CHARTS) {
	    Chart.UpdateCharts();
            _updating = UPDATING_DISPLAYS;
        } else if (_updating == UPDATING_DISPLAYS) {
	    Chart.FlushChartBuffers();
            if (_dirty) {
                _updating = UPDATING_CHARTS;
                MarkClean();
            } else {
                _updating = UPDATING_NONE;
            }
        }
 */

	long load_avg = (long)Chart.Find(ZIScript.CHART_LOAD).Avg;
	long time_avg = (long)Chart.Find(ZIScript.CHART_TIME).Avg;
	long time_subs_avg = 0; // FIXME
	long datapoints_tot = (long)Chart.Find(CHART_DATAPOINTS).Sum;
	Log($"[Cycle {_cycles}]\n  [Avg ] Load {load_avg}% in {time_avg}us (Rx: {time_subs_avg}us/{datapoints_tot} points)");

	for (int i = 0; i < 16; i++) {
	    long load = (long)Chart.Find(ZIScript.CHART_LOAD).Datapoint(-i);
	    long time = (long)Chart.Find(ZIScript.CHART_TIME).Datapoint(-i);
	    long time_subs = 0; // FIXME (long)Chart.Find(CHART_SUBS_TIME).Datapoint(-i);
	    long count_datapoints = (long)Chart.Find(CHART_DATAPOINTS).Datapoint(-i);
	    Log($"  [T-{i,-2}] Load {load}% in {time}us (Rx: {time_subs}us/{count_datapoints} points)");
	}
	Log($"Charts: {Chart.InstanceCount}, DrawBuffers: {Chart.BufferCount}");
    }
}

public void MarkDirty() {
    _dirty = true;
}

public void MarkClean() {
    _dirty = false;
}

public void CommandCreate(string command, MyCommandLine command_line, string full_argument) {
    // eg: create "string chart_name" "string unit"
    int first_arg = 1;
    string arg_chart_name = command_line.Argument(first_arg + 0);
    string arg_unit = command_line.Argument(first_arg + 1);

    Chart.Create(arg_chart_name, arg_unit);
    _zis.Subscribe($"datapoint.issue.{arg_chart_name}", this.EventDatapointIssue);
}

public void CommandAdd(string command, MyCommandLine command_line, string full_argument) {
    // eg: add "string chart_name" <double value>
    int first_arg = 1;
    string arg_chart_name = command_line.Argument(first_arg + 0);
    double arg_value = double.Parse(command_line.Argument(first_arg + 1), System.Globalization.CultureInfo.InvariantCulture);

    Chart.Find(arg_chart_name).AddDatapoint(arg_value);
}

public void EventDatasetCreate(string event_name, object data) {
    // dataset.create <string chart_name, string unit>
    MyTuple<string, string> d = (MyTuple<string, string>)data;
    Chart.Create(d.Item1, d.Item2);
    _zis.Subscribe($"datapoint.issue.{d.Item1}", this.EventDatapointIssue);
}

public void EventDatapointIssue(string event_name, object data) {
    // datapoint.issue.<chart_name> <string chart_name, double value>
    MyTuple<string, double> d = (MyTuple<string, double>)data;
    //Warning($"rx datapoint {d.Item1}={d.Item2}");
    Chart.Find(d.Item1).AddDatapoint(d.Item2);
    _last_run_datapoints++;
}

// FIXME just chart panels
public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
	_panels[i].Clear();
	GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(_panels[i], block => ((IMyTerminalBlock)block).CustomName.Contains(_panel_tags[i]) && ((IMyTerminalBlock)block).IsSameConstructAs(Me));
	if (i == PANELS_CHART)
	    continue;
	for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
	    IMyTextPanel panel = _panels[i][j] as IMyTextPanel;
	    panel.ContentType = ContentType.TEXT_AND_IMAGE;
	    panel.Font = "Monospace";
	    panel.FontSize = 0.5F;
	    panel.TextPadding = 0.5F;
	    panel.Alignment = TextAlignment.LEFT;
	}
    }
    Chart.UpdatePanels(_panels[PANELS_CHART]);
}

// FIXME split into new helper
public class DrawBuffer {
    static public Program Program { set; get; }

    public IMyTextSurface Surface;
    private List<MySprite?> Buffer;
    public Vector2 Size, Offset;

    public bool IsDirty { get; private set; }

    public DrawBuffer(IMyTextSurface surface) {
	surface.ContentType = ContentType.SCRIPT;
	surface.Script = null;
	Offset = (surface.TextureSize - surface.SurfaceSize) / 2f;
	Size = surface.SurfaceSize * 1f;
	Buffer = new List<MySprite?>();
	Surface = surface;
	IsDirty = true;
    }

    // FIXME: explicit layers?
    // FIXME: slot class with slot index property?
    public List<int> Reserve(int reserve) {
	List<int> reserved = new List<int>(reserve);
	int slot = Buffer.Count();

	// TODO: should allow release of old slots and reuse? order matters though.
	for (int i = 0; i < reserve; i++) {
	    Buffer.Add(null);
	    reserved.Add(slot + i);
	}

	return reserved;
    }

    public void Write(int slot, MySprite sprite) {
	//DrawBuffer.Program.Log($"Write {slot}.");
	Buffer[slot] = sprite;
	IsDirty = true;
    }

    public void Flush() {
	if (Surface != null) {
	    using (var frame = Surface.DrawFrame()) {
		foreach (MySprite? sprite in Buffer) {
		    if (sprite != null) {
			//DrawBuffer.Program.Log($"Frame add p{sprite.Value.Position} s{sprite.Value.Size}.");
			frame.Add(sprite.Value);
		    }
		}
	    }
	}
	IsDirty = false;
    }
}

public class Viewport {
    public DrawBuffer Buffer;
    public Vector2 Offset = new Vector2(), Size = new Vector2();

    public bool IsDirty {
	get { return Buffer.IsDirty; }
	//set { Buffer.IsDirty = value; }
    }

    public Viewport(DrawBuffer buffer, Vector2 offset, Vector2 size) {
	Buffer = buffer;
	Offset = Buffer.Offset + offset;
	Size = size;
    }

    public Viewport SubViewport(Vector2 offset, Vector2 size) {
	return new Viewport(Buffer, Offset - Buffer.Offset + offset, size);
    }

    public List<int> Reserve(int reserve) {
	return Buffer.Reserve(reserve);
    }

    public void Write(int slot, MySprite sprite) {
	// TODO: Yeah, no bounds checking. Sue me.
	Buffer.Write(slot, sprite);
    }
}

public class ChartOptions {
    public bool Horizontal, ShowTitle, ShowCur, ShowAvg, ShowMax, ShowScale;
    public string Title, Unit, Font, CurLabel, AvgLabel, MaxLabel, ScaleLabel;
    public int NumBars;
    public Color FgColor, BgColor, GoodColor, BadColor;
    public float WarnAbove, WarnBelow, FontSize, FramePadding, Scaling;

    public ChartOptions(Color fg_color, Color bg_color, Color good_color, Color bad_color,
	bool horizontal = true,
	bool show_title = true,
	bool show_cur = true, bool show_avg = true, bool show_max = false, bool show_scale = true,
	string cur_label = "cur:", string avg_label = "avg:", string max_label = "max:",
	string scale_label = "Y:",
	string title = "", string unit = "", float scaling = 1f,
	int num_bars = 30,
	float warn_above = Single.NaN, float warn_below = Single.NaN,
	string font = "Monospace", float font_size = 0.6f, float frame_padding = 24f) {

	Horizontal = horizontal;
	ShowTitle = show_title;
	ShowCur = show_cur;
	ShowAvg = show_avg;
	ShowMax = show_max;
	ShowScale = show_scale;
	CurLabel = cur_label;
	AvgLabel = avg_label;
	MaxLabel = max_label;
	ScaleLabel = scale_label == "Y:" ? (Horizontal ? "Y:" : "X:") : scale_label;
	Title = title;
	Unit = unit;
	Scaling = scaling;
	NumBars = num_bars;
	FgColor = fg_color;
	BgColor = bg_color;
	GoodColor = good_color;
	BadColor = bad_color;
	WarnAbove = warn_above;
	WarnBelow = warn_below;
	Font = font;
	FontSize = font_size;
	FramePadding = frame_padding;
    }
}

public class SlottedSprite {
    static public Program Program { set; get; }

    public Viewport Viewport { get; private set; }
    public int Slot { get; private set; }

    public Vector2? Position { get; set; }
    public Vector2? Size { get; set; }
    public Color Color { get; set; }

    private MySprite? _sprite;
    public MySprite? Sprite {
	get { return _sprite; }
	set {
	    _sprite = value;
	    if (_sprite != null) {
		Size = _sprite?.Size;
		Color = (Color)_sprite?.Color;
	    }
	}
    }

    public SlottedSprite(Viewport viewport, MySprite? sprite) {
	Viewport = viewport;
	Slot = Viewport.Reserve(1)[0];
	Sprite	 = sprite;
	Size	 = Sprite?.Size	    ?? new Vector2(0f);
	Position = Sprite?.Position ?? new Vector2(0f);
	Color	 = Sprite?.Color    ?? new Color(1f);
    }

    public void Write() {
	if (Sprite != null) {
	    // TODO: Yeah, no bounds checking. Sue me.
	    MySprite sprite = Sprite.Value;
	    sprite.Position = Viewport.Offset + Position;
	    sprite.Size = Size;
	    sprite.Color = Color;
	    //SlottedSprite.Program.Log($"SlottedSprite.Write p{sprite.Position} s{sprite.Size} O{Viewport.Offset} P{Position}");
	    Viewport.Write(Slot, sprite);
	}
    }
}

// TODO: move frame stuff into an interface and share with a TextDisplay.
public class ChartDisplay {
    static public Program Program { set; get; }

    public Viewport FrameViewport;
    public Viewport ChartViewport;
    public ChartOptions Options;

    public int? CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public double? SampleCur;
    public double SampleTotal, SampleMax, Scale;
    public int NumSamples;

    private List<SlottedSprite> Bars, Frame;

    const int FRAME_BG		  = 0;
    const int FRAME_BORDER	  = 1;
    const int FRAME_INNER_BG	  = 2;
    const int FRAME_TITLE_BORDER  = 3;
    const int FRAME_TITLE_BG	  = 4;
    const int FRAME_TITLE	  = 5;
    const int FRAME_STATUS_BORDER = 6;
    const int FRAME_STATUS_BG	  = 7;
    const int FRAME_STATUS	  = 8;
    const int SIZE_FRAME	  = 9;

    public bool IsDirty {
	get { return FrameViewport.IsDirty; } // FIXME: needed anymore?
	//set { Viewport.IsDirty = value; }
    }

    public ChartDisplay(Viewport viewport, ChartOptions options) {
	FrameViewport = viewport;
	Options = options;

	Vector2 padding = new Vector2(Options.FramePadding);
	ChartViewport = FrameViewport.SubViewport(padding + 2f, (FrameViewport.Size - padding * 2f) - 4f);

	ConfigureViewport();
    }

    public void ConfigureViewport() {
	ConfigureFrameViewport();
	ConfigureChartViewport();
    }

    public void ConfigureFrameViewport() {
	string label;
	MySprite sprite;

	Frame = new List<SlottedSprite>(SIZE_FRAME);
	for (int i = 0; i < SIZE_FRAME; i++) {
	    Frame.Add(new SlottedSprite(FrameViewport, null));
	}
	// Two square sprites so we get a consistent line thickness, rather than scaling SquareHollow
	Vector2 outer = new Vector2((float)(((int)Options.FramePadding / 2) - 1));
	Vector2 outer_size = FrameViewport.Size - outer * 2f;
	Vector2 inner = new Vector2((float)(((int)Options.FramePadding / 2) + 1));
	Vector2 inner_size = FrameViewport.Size - inner * 2f;

	//ChartDisplay.Program.Warning($"outer:{outer} size:{outer_size}\n    inner:{inner} size:{inner_size}");
	Frame[FRAME_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
	    size: FrameViewport.Size,
	    color: new Color(0f, 0.35f, 0.6f));
	Frame[FRAME_BG].Position = FrameViewport.Size / 2f;
	//Frame[FRAME_BG].Size = FrameViewport.Size;
	Frame[FRAME_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
	    size: outer_size,
	    color: Options.FgColor);
	Frame[FRAME_BORDER].Position = FrameViewport.Size / 2f;
	//Frame[FRAME_BORDER].Size = outer_size;
	Frame[FRAME_INNER_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
	    size: inner_size,
	    color: Options.BgColor);
	Frame[FRAME_INNER_BG].Position = FrameViewport.Size / 2f;
	//Frame[FRAME_INNER_BG].Size = inner_size;

	if (Options.ShowTitle) {
	    label = $"{Options.Title}";
	    sprite = MySprite.CreateText(label, Options.Font, Options.FgColor, Options.FontSize, TextAlignment.CENTER);
	    sprite.Size = FrameViewport.Buffer.Surface.MeasureStringInPixels(new StringBuilder(label), Options.Font, Options.FontSize);
	    //SlottedSprite.Program.Warning($"	setting size {sprite.Size} ({label})");
	    if (sprite.Size?.X < inner_size.X) {
		Vector2 border = new Vector2((float)((int)sprite.Size?.X + 11), Options.FramePadding - 2f);
		//SlottedSprite.Program.Warning($"  Title size fits");
		Frame[FRAME_TITLE].Sprite = sprite;
		Frame[FRAME_TITLE].Position = new Vector2(FrameViewport.Size.X / 2f, (Options.FramePadding - (float)sprite.Size?.Y) / 2f);
		Frame[FRAME_TITLE_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
		    size: border - 2f,
		    color: Options.BgColor);
		Frame[FRAME_TITLE_BG].Position = new Vector2(FrameViewport.Size.X / 2f, Options.FramePadding / 2f);
		//Frame[FRAME_TITLE_BG].Size = Frame[FRAME_TITLE_BG].Sprite?.Size;
		Frame[FRAME_TITLE_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
		    size: border,
		    color: Options.FgColor);
		Frame[FRAME_TITLE_BORDER].Position = new Vector2(FrameViewport.Size.X / 2f, Options.FramePadding / 2f);
		//Frame[FRAME_TITLE_BORDER].Size = Frame[FRAME_TITLE_BORDER].Sprite?.Size;
	    }
	}
    }

    public void ConfigureChartViewport() {
	Bars = new List<SlottedSprite>(Options.NumBars);
	for (int i = 0; i < Options.NumBars; i++) {
	    // FIXME: configure thickness and time axis position
	    Bars.Add(new SlottedSprite(ChartViewport,
		new MySprite(SpriteType.TEXTURE, "SquareSimple",
		    size: new Vector2(50f),
		    color: Options.FgColor)));
	}
    }

    public void StartDraw() {
	SampleCur   = null;
	SampleTotal = 0.0;
	SampleMax   = 0.0;
	Scale	    = 0.0;
	NumSamples  = 1;
    }

    public void EndDraw() {
	DrawFrame();
    }

    public void DrawFrame() {
	string label;

	if (Options.ShowCur || Options.ShowAvg || Options.ShowMax || Options.ShowScale) {
	    List<string> segments = new List<string>(2);
	    if (Options.ShowCur) {
		label = $"{Options.CurLabel}{SampleCur * (double)Options.Scaling,5:G4}{Options.Unit}";
		segments.Add(label);
	    }
	    if (Options.ShowAvg) {
		float avg = Options.Scaling * (float)SampleTotal / (float)NumSamples;
		label = $"{Options.AvgLabel}{avg,5:G4}{Options.Unit}";
		segments.Add(label);
	    }
	    if (Options.ShowMax) {
		label = $"{Options.MaxLabel}{SampleMax * (double)Options.Scaling,5:G4}{Options.Unit}";
		segments.Add(label);
	    }
	    if (Options.ShowScale) {
		label = $"{Options.ScaleLabel}{Scale * (double)Options.Scaling,6:G5}{Options.Unit}";
		segments.Add(label);
	    }
	    label = string.Join(" ", segments);
	    // FIXME: this should be saved only built once in configure
	    Vector2 inner = new Vector2((float)(((int)Options.FramePadding / 2) + 1));
	    Vector2 inner_size = FrameViewport.Size - inner * 2f;
	    MySprite sprite = MySprite.CreateText(label, Options.Font, Options.FgColor, Options.FontSize, TextAlignment.CENTER);
	    sprite.Size = FrameViewport.Buffer.Surface.MeasureStringInPixels(new StringBuilder(label), Options.Font, Options.FontSize);
	    //SlottedSprite.Program.Warning($"	setting status size {sprite.Size} ({label})");
	    if (sprite.Size?.X < inner_size.X) {
		Vector2 border = new Vector2((float)((int)sprite.Size?.X + 11), Options.FramePadding - 2f);
		Frame[FRAME_STATUS].Sprite = sprite;
		Frame[FRAME_STATUS].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - Options.FramePadding + (Options.FramePadding - (float)sprite.Size?.Y) / 2f);
		// FIXME: init sprite in configure, only resize here
		Frame[FRAME_STATUS_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
		    size: border - 2f,
		    color: Options.BgColor);
		Frame[FRAME_STATUS_BG].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - Options.FramePadding / 2f);
		//Frame[FRAME_STATUS_BG].Size = Frame[FRAME_STATUS_BG].Sprite?.Size;
		// FIXME: init sprite in configure, only resize here
		Frame[FRAME_STATUS_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
		    size: border,
		    color: Options.FgColor);
		Frame[FRAME_STATUS_BORDER].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - Options.FramePadding / 2f);
		//Frame[FRAME_STATUS_BORDER].Size = Frame[FRAME_STATUS_BORDER].Sprite?.Size;
	    }
	}

	for (int i = 0; i < SIZE_FRAME; i++) {
	    //ChartDisplay.Program.Log($"Writing frame {i}");
	    Frame[i].Write();
	}
    }

    public void DrawBars() {
	// FIXME: move in here and access dataset ourselves.
    }

    // TODO: could use circular buffer and only update position, not size.
    public void DrawBar(int t, double val, double max) {
	//ChartDisplay.Program.Log($"DrawBarToVP top");

	int slot = Bars.Count - t - 1;

	if (slot < 0)
	    return;

	// FIXME: Hmm, this is awkward if we aren't redrawing all bars.
	if (!SampleCur.HasValue)
	    SampleCur = val;
	SampleTotal += val;
	if (val > SampleMax)
	    SampleMax = val;
	Scale = max;
	NumSamples++;

	SlottedSprite slotted_sprite = Bars[slot];

	if (Single.IsNaN(Options.WarnAbove) && Single.IsNaN(Options.WarnBelow)) {
	    slotted_sprite.Color = Options.FgColor;
	} else {
	    if (!Single.IsNaN(Options.WarnAbove) && (val > (double)Options.WarnAbove)) {
		slotted_sprite.Color = Options.BadColor;
	    } else if (!Single.IsNaN(Options.WarnBelow) && (val < (double)Options.WarnBelow)) {
		slotted_sprite.Color = Options.BadColor;
	    } else {
		slotted_sprite.Color = Options.GoodColor;
	    }
	}

	Vector2 breadth_mask, length_mask;

	// FIXME: constantize and unroll the static vectors.

	// Funky vector crud to ensure we act on the correct dimension of the vectors.
	if (Options.Horizontal) {
	    breadth_mask = new Vector2(1f, 0f);
	    length_mask = new Vector2(0f, 1f);
	} else {
	    breadth_mask = new Vector2(0f, 1f);
	    length_mask = new Vector2(1f, 0f);
	}

	//ChartDisplay.Program.Log($"DrawBarToVP t{t}, v{val}, m{max}");
	// FIXME: constantize and unroll the static factors (mask, size, max)
	slotted_sprite.Size = (length_mask * ChartViewport.Size * (float)val / (float)max) +
	    (breadth_mask * ((ChartViewport.Size / (float)Bars.Count) - 2f));
	// X/Y axis go in opposite directions and start from opposite ends, hence the length madness
	// FIXME: static vectors
	slotted_sprite.Position = (breadth_mask * ((float)slot + 0.5f) * ChartViewport.Size / (float)Bars.Count) +
	    (length_mask * new Vector2(-1f, 1f) * (new Vector2(0f, 1f) * ChartViewport.Size - slotted_sprite.Size / 2f));
	//ChartDisplay.Program.Log($"DrawBarToVP t{t}, v{val}, m{max}\n	   s{slotted_sprite.Size} p{slotted_sprite.Position}");
	slotted_sprite.Write();
    }
}

public class Chart {
    static public Program Program { set; get; }

    // title => Chart
    static private Dictionary<string, Chart> _charts = new Dictionary<string, Chart>();
    static public int InstanceCount { get { return _charts.Count(); } }
    static private IEnumerator<Chart> _chart_iterator = null;

    static private Dictionary<long, int> _config_hashes = new Dictionary<long, int>();
    // (panel.EntityId * 1000) + surface_id => DrawBuffer
    static private Dictionary<long, DrawBuffer> _chart_buffers = new Dictionary<long, DrawBuffer>();
    static public int BufferCount { get { return _chart_buffers.Count(); } }
    static private IEnumerator<DrawBuffer> _buffer_iterator = null;

    private List<ChartDisplay> displays;
    private Dataset dataset;

    public string Title { get; private set; }
    public string Unit { get; private set; }

    public bool IsViewed { get { return displays.Count > 0; } }
    public double Max { get { return dataset.Max; } }
    public double Sum { get { return dataset.Sum; } }
    public double Avg { get { return dataset.Avg; } }
    public int Count { get { return dataset.Count; } }
    public bool IsDataDirty { get { return dataset.IsDirty; } }
    public bool IsDisplayDirty {
	get { return displays.Any(display => display.IsDirty); }
	//set { foreach (ChartDisplay display in displays) display.IsDirty = value; }
    }

    /* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
    static MyIni _ini = new MyIni();

    static public Chart Find(string title) {
	Chart chart;
	if (!_charts.TryGetValue(title, out chart)) {
	    chart = new Chart(title);
	    _charts.Add(title, chart);
	} else {
	    // FIXME: probably should error or warn or something?
	}
	return chart;
    }

    static public Chart Create(string title, string unit) {
	Chart chart = Find(title);
	if (chart.Unit != unit) {
	    // FIXME: make this default_unit and add default_title
	    chart.Unit = unit;
	    chart.ConfigureViewports();
	}
	return chart;
    }

    private Chart(string title) {
	Dataset.Program = Program; // h4x
	displays = new List<ChartDisplay>();
	Title = title;
	Unit = "";
	dataset = new Dataset();
    }

    public void AddDatapoint(double datapoint) {
	dataset.AddDatapoint(datapoint);
    }

    public double Datapoint(int offset) {
	return dataset.Datapoint(offset);
    }

    public void AddViewport(Viewport viewport, ChartOptions options) {
	ChartDisplay display = new ChartDisplay(viewport, options);

	displays.Add(display);
    }
    public void AddBuffer(DrawBuffer buffer, Vector2 offset, Vector2 size, ChartOptions options) {
	AddViewport(new Viewport(buffer, offset, size), options);
    }

    public void ConfigureViewports() {
	for (int d = 0, sz = displays.Count; d < sz; d++) {
	    displays[d].Options.Title = Title; // Ew. Codesmell.
	    displays[d].Options.Unit = Unit;
	    displays[d].ConfigureViewport();
	}
    }

    public void RemoveDisplays() {
	displays.Clear();
    }

    public void RemoveDisplaysForBuffer(DrawBuffer buffer) {
	displays.RemoveAll(display => display.FrameViewport.Buffer == buffer);
    }

    public void DrawBar(int t, double val, double max) {
	for (int d = 0, sz = displays.Count; d < sz; d++) {
	    displays[d].DrawBar(t, val, max);
	}
    }

    public void StartDraw() {
	for (int d = 0, sz = displays.Count; d < sz; d++) {
	    displays[d].StartDraw();
	}
    }

    public void EndDraw() {
	for (int d = 0, sz = displays.Count; d < sz; d++) {
	    displays[d].EndDraw();
	}
    }

    public void DrawChart() {
	//if (IsViewed && dataset.IsDirty) {
	if (IsViewed) {
	    double max = Max;
	    StartDraw();
	    for (int i = 0; i < HISTORY; i++) {
		//Log($"Update T-{i,-2}");
		DrawBar(i, dataset.Datapoint(-i), max);
	    }
	    EndDraw();
	    dataset.Clean();
	}
    }

    static public bool DrawNextChart() {
        if (_chart_iterator == null) {
            _chart_iterator = _charts.ToImmutableDictionary().Values.GetEnumerator();
        }
        if (!_chart_iterator.MoveNext()) {
            _chart_iterator = null;
            return false;
        }

        _chart_iterator.Current.DrawChart();
        return true;
    }

    static public void SimpleDrawCharts() {
	//ResetChartBuffers();
	// FIXME: Ideally should spread this over different updates. Messes with composition though.
	foreach (Chart chart in _charts.Values) {
	    chart.DrawChart();
	}
    }

    static public void MinimalDrawCharts() {
	foreach (Chart chart in _charts.Values) {
	    if (chart.IsDataDirty) {
		// FIXME: chart.IsDisplayDirty = true;
	    }
	}
	//ResetDirtyChartBuffers();
	// FIXME: Ideally should spread this over different updates. Messes with composition though.
	foreach (Chart chart in _charts.Values) {
	    if (chart.IsDisplayDirty) {
		chart.DrawChart();
	    }
	}
    }

    static public void DrawCharts() {
	UpdateCharts();
        FlushChartBuffers();
    }

    static public void UpdateCharts() {
	SimpleDrawCharts();
    }

    static public void FlushChartBuffers() {
        SimpleFlushChartBuffers();
    }

    static public bool FlushNextChartBuffer() {
        if (_buffer_iterator == null) {
            _buffer_iterator = _chart_buffers.ToImmutableDictionary().Values.GetEnumerator();
        }
        if (!_buffer_iterator.MoveNext()) {
            _buffer_iterator = null;
            return false;
        }

        _buffer_iterator.Current.Flush();
        return true;
    }

    /* FIXME needed?
    static public void ResetChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Reset();
	}
    }
     */

    static public void SimpleFlushChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Flush();
	}
    }

    static public void FlushDirtyChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    if (buffer.IsDirty) {
		buffer.Flush();
	    }
	}
    }

    static public Color ColorFromHex(string hex) {
	long rgb;
	Color col;

	if (hex[0] == '#') {
	    hex = hex.Substring(1);
	}

	if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out rgb)) {
	    col = new Color(
		(float)((rgb % (256 * 256 * 256)) / (256 * 256)) / 255f,
		(float)((rgb % (256 * 256)) / 256) / 255f,
		(float)(rgb % 256) / 255f
		);
	} else {
	    ChartDisplay.Program.Warning($"Unable to parse colour from hex value '{hex}'");
	    col = new Color(1f, 0f, 1f);
	}
	//ChartDisplay.Program.Warning($"Parsed {hex} into {col} from {rgb}");
	return col;
    }

    // TODO: make extension?
    static public int GetSurfaceIdWithName(IMyTextSurfaceProvider provider, string name) {
	int parsed;
	if (int.TryParse(name, out parsed)) {
	    return parsed;
	}
	for (int i = 0; i < provider.SurfaceCount; i++) {
	    if (provider.GetSurface(i).DisplayName == name) {
		return i;
	    }
	}
	return -1;
    }

    // FIXME: This should be time-sliced to one panel parse per update.
    static public void UpdatePanels(List<IMyTextSurfaceProvider> panels) {
	// Special logic for ChartPanels, need to set up buffers and read their config.
	HashSet<long> found_ids = new HashSet<long>(panels.Count);
	DrawBuffer buffer;

	MyIniParseResult parse_result;
	List<string> sections = new List<string>();
	Chart chart;
	int width, height, x, y, num_bars, config_hash, surface_id;
	long combo_id;
	bool horizontal, show_title, show_cur, show_avg, show_max, show_scale;
	float warn_above, warn_below, font_size, frame_padding, scaling;
	string name, title, unit, surface_name, font, cur_label, avg_label, max_label, scale_label;
	Color fg_color, bg_color, good_color, bad_color;

	for (int i = 0, sz = panels.Count; i < sz; i++) {
	    IMyTerminalBlock panel = (IMyTerminalBlock)panels[i];
	    long id = panel.EntityId;

	    if (_config_hashes.TryGetValue(id, out config_hash)) {
		if (panel.CustomData.GetHashCode() == config_hash) {
		    //Program.Warning($"Chart panel skipping unchanged config parse on panel '{panel.CustomName}'");
		    // Mild hack here... it doesn't matter if we mark unconfigured surfaces as seen.
		    for (int j = 0; j < ((IMyTextSurfaceProvider)panel).SurfaceCount; j++) {
			combo_id = (id * 1000) + (long)j; // h4x
			found_ids.Add(combo_id);
		    }
		    continue;
		}
	    } else {
		_config_hashes.Add(id, panel.CustomData.GetHashCode());
	    }

	    if (!_ini.TryParse(panel.CustomData, out parse_result)) {
		Program.Warning($"Chart panel parse error on panel '{panel.CustomName}' line {parse_result.LineNo}: {parse_result.Error}");
		found_ids.Remove(id); // Move along. Nothing to see. Pretend we never saw the panel.
		continue;
	    }
	    _ini.GetSections(sections);
	    foreach (string section in sections) {
		//Program.Warning($"Found section {section}");
		name = _ini.Get(section, "chart").ToString(section);
		surface_name = _ini.Get(section, "surface").ToString("0");
		chart = Find(name);
		/*
		if (!chart)) {
		    Program.Warning($"Chart panel '{panel.CustomName}' error in section '{section}': '{name}' is not the name of a known chart type."); // FIXME: list chart names
		    continue;
		}
		*/
		width = _ini.Get(section, "width").ToInt32(100);
		height = _ini.Get(section, "height").ToInt32(100);
		x = _ini.Get(section, "x").ToInt32(0);
		y = _ini.Get(section, "y").ToInt32(0);
		// horizontal, etc ChartOptions settings.
		horizontal = _ini.Get(section, "horizontal").ToBoolean(true);
		show_title = _ini.Get(section, "show_title").ToBoolean(true);
		show_cur = _ini.Get(section, "show_cur").ToBoolean(true);
		show_avg = _ini.Get(section, "show_avg").ToBoolean(true);
		show_max = _ini.Get(section, "show_max").ToBoolean(false);
		show_scale = _ini.Get(section, "show_scale").ToBoolean(true);
		cur_label = _ini.Get(section, "cur_label").ToString("cur:");
		avg_label = _ini.Get(section, "avg_label").ToString("avg:");
		max_label = _ini.Get(section, "max_label").ToString("max:");
		scale_label = _ini.Get(section, "scale_label").ToString("Y:");
		title = _ini.Get(section, "title").ToString(name);
		unit = _ini.Get(section, "unit").ToString(chart.Unit); // FIXME: prob gets overwritten by create commands
		scaling = _ini.Get(section, "scaling").ToSingle(1f);
		num_bars = _ini.Get(section, "bars").ToInt32(30);
		warn_above = _ini.Get(section, "warn_above").ToSingle(Single.NaN);
		warn_below = _ini.Get(section, "warn_below").ToSingle(Single.NaN);
		fg_color = ColorFromHex(_ini.Get(section, "fg_color").ToString("#D0D0D0"));
		bg_color = ColorFromHex(_ini.Get(section, "bg_color").ToString("#000000"));
		good_color = ColorFromHex(_ini.Get(section, "good_color").ToString("#00D000"));
		bad_color = ColorFromHex(_ini.Get(section, "bad_color").ToString("#D00000"));
		font = _ini.Get(section, "font").ToString("Monospace");
		font_size = _ini.Get(section, "font_size").ToSingle(1f);
		frame_padding = _ini.Get(section, "frame_padding").ToSingle(24f);
		// TODO: add border_width

		width = System.Math.Min(System.Math.Max(width, 0), 100);
		height = System.Math.Min(System.Math.Max(height, 0), 100);
		x = System.Math.Min(System.Math.Max(x, 0), 100);
		y = System.Math.Min(System.Math.Max(y, 0), 100);
		font_size *= 0.6f; // Normalize at 0.6f.

		// Rescale these into actual unit values, rather than the user's ones.
		warn_above /= scaling;
		warn_below /= scaling;

		surface_id = GetSurfaceIdWithName((IMyTextSurfaceProvider)panel, surface_name);
		if (surface_id == -1) {
		    Program.Warning($"Unable to find surface with name: \"{surface_name}\" on \"{panel.CustomName}\"");
		    continue;
		}

		combo_id = (id * 1000) + (long)surface_id; // h4x
		found_ids.Add(combo_id);
		//ChartDisplay.Program.Warning($"Configuring: \"{panel.CustomName}\"");
		if (!_chart_buffers.TryGetValue(combo_id, out buffer)) {
		    buffer = new DrawBuffer(((IMyTextSurfaceProvider)panel).GetSurface(surface_id));
		    //Program.Warning($"New panel: \"{panel.Name}\"");
		    //Program.Warning($"  panel: \"{panel.DefinitionDisplayNameText}\"");
		    // FIXME: this may be localized???
		    // FIXME: should probably check it's surface 0 just to be safe.
		    if (panel.DefinitionDisplayNameText.Contains("Corner LCD")) {
			// Correct corner LCD surface sizes
			if (panel.DefinitionDisplayNameText.Contains("Corner LCD Flat")) {
			    buffer.Size.Y = 88f;
			} else {
			    buffer.Size.Y = 76f;
			}
			//Program.Warning($"fixing panel: \"{panel.CustomName}\", s{buffer.Size}");
		    }
		    _chart_buffers.Add(combo_id, buffer);
		}


		// Hmm, removing it here means we can't have multiples of same chart on same panel
		// TODO: maybe keep track of those chart names we've removed already in the sections loop?
		chart.RemoveDisplaysForBuffer(buffer);
		// FIXME: clamp width/height, x/y
		chart.AddBuffer(buffer,
		    buffer.Size * new Vector2((float)x, (float)y) / 100f,
		    buffer.Size * new Vector2((float)width, (float)height) / 100f,
		    new ChartOptions(
			horizontal: horizontal,
			show_title: show_title,
			show_cur: show_cur,
			show_avg: show_avg,
			show_max: show_max,
			show_scale: show_scale,
			cur_label: cur_label,
			avg_label: avg_label,
			max_label: max_label,
			scale_label: scale_label,
			title: title,
			unit: unit,
			scaling: scaling,
			num_bars: num_bars,
			warn_above: warn_above,
			warn_below: warn_below,
			fg_color: fg_color,
			bg_color: bg_color,
			good_color: good_color,
			bad_color: bad_color,
			font: font,
			font_size: font_size,
			frame_padding: frame_padding
			));
	    }
	}
	// Prune old ids in _chart_buffers
	HashSet<long> old_ids = new HashSet<long>(_chart_buffers.Keys);
	old_ids.ExceptWith(found_ids);
	foreach (long missing_id in old_ids) {
	    foreach (Chart mychart in _charts.Values) {
		mychart.RemoveDisplaysForBuffer(_chart_buffers[missing_id]);
	    }
	    _chart_buffers.Remove(missing_id);
	}
    }
}

class Dataset {
    static public Program Program { set; get; }

    private List<double> datapoints = new List<double>(HISTORY);

    public bool IsDirty { get; private set; }
    public int Counter { get; private set; }
    public int Count { get { return datapoints.Count(); } }
    public double Max { get { return datapoints.Max(); } }
    public double Sum { get; private set; }
    public double Avg { get { return Sum / Count; } }

    public Dataset() {
	Counter = 0;
	//datapoints = new List<double>(HISTORY);
	for (int i = 0; i < HISTORY; i++) {
	    datapoints.Add(0.0);
	}
	IsDirty = true;
    }

    public int SafeMod(int val, int mod) {
	while (val < 0)
	    val += mod;
	return val % mod;
    }
    private int Offset(int delta) { return SafeMod(Counter + delta, HISTORY); }

    public double Datapoint(int offset) {
	return datapoints[Offset(offset)];
    }

    public void AddDatapoint(double datapoint) {
	Counter++;
	int now = Offset(0);
	Sum += datapoint - datapoints[now];
	datapoints[now] = datapoint;
	IsDirty = true;
        Program.MarkDirty();
    }

    public void Clean() {
	IsDirty = false;
    }
}

class ZIScript {
    public const string ZIS_VERSION = "3.0.2";

    public const string SCRIPT_TITLE = Program.SCRIPT_FULL_NAME + " v" + Program.SCRIPT_VERSION + " (ZIS v" + ZIS_VERSION + ")";
    public const string SCRIPT_TITLE_NL = SCRIPT_TITLE + "\n";

    public const string CHART_TIME = Program.SCRIPT_ID + " Exec Time";
    public const string CHART_MAIN_TIME = Program.SCRIPT_ID + " Main Loop Time";
    public const string CHART_EVENTS_TIME = Program.SCRIPT_ID + " Events Time";
    public const string CHART_LOAD = Program.SCRIPT_ID + " Instr Load";
    public const string CHART_EVENTS_RX = Program.SCRIPT_ID + " Events Rx";
    public const string CHART_EVENTS_TX = Program.SCRIPT_ID + " Events Tx";
    public const string CHART_EVENTS_BANDWIDTH = Program.SCRIPT_ID + " Events Bandwidth";

    public const string PUBSUB_BROADCAST_PREFIX = "ZIPubSub";

    const int SIZE_PANELS  = 2;

    class ZIPubSubSubscription {
	public IMyBroadcastListener Listener;
	public Action<string, object> Handler;

	public ZIPubSubSubscription(IMyBroadcastListener listener, Action<string, object> handler) {
	    Listener = listener;
	    Handler = handler;
	}
    }

    int _debug_panels, _warning_panels;

    int _cycles = 0;

    List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
    List<string> _panel_text = new List<string>(SIZE_PANELS);
    List<string> _panel_tags = new List<string>(SIZE_PANELS);

    Dictionary<string, ZIPubSubSubscription> _subscriptions = new Dictionary<string, ZIPubSubSubscription>();
    Dictionary<string, Action<string, MyCommandLine, string>> _commands = new Dictionary<string, Action<string, MyCommandLine, string>>();

    double _time_total = 0.0;

    bool _log_events = false;
    bool _send_data_events_to_self = false;

    public const int LAST_RUN_NONE  = -1;
    public const int LAST_RUN_MAIN  = 0;
    public const int LAST_RUN_EVENT = 1;
    public const int SIZE_LAST_RUN  = 2;

    int _last_run = LAST_RUN_MAIN; // h4x so constructor gets logged in main

    class Tally {
	public int Cycles;
	public double Time;
	public int Instr;
	public int Rx;
	public int Tx;
    }

    List <Tally> _tallies = new List<Tally>(SIZE_LAST_RUN);

    class MaxEvent {
        public double Utilization;
        public int Rx;
        public int Max;
        public string Channel;
    }

    MaxEvent _max_event = new MaxEvent() { Utilization = 0.0, Rx = 0, Max = 0, Channel = "" };

    // Delegates to Program instance for convenience.
    Program Prog;
    Action<string> Echo;
    IMyProgrammableBlock Me;

    Action<UpdateType> _mainloop_handler;

    /* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
    MyCommandLine _command_line = new MyCommandLine();

    // FIXME: log_events is unused as yet
    public ZIScript(Program prog, Action<UpdateType> mainloop_handler = null, bool log_events = false, bool send_data_events_to_self = false) {
	Prog = prog;
	_mainloop_handler = mainloop_handler;
	_log_events = log_events;
	_send_data_events_to_self = send_data_events_to_self;

	// take reference to Me, and Echo
	Echo = Prog.Echo;
	Me = Prog.Me;

	for (int i = 0; i < SIZE_LAST_RUN; i++) {
	    _tallies.Add(new Tally() { Cycles = 0, Time = 0.0, Instr = 0, Rx = 0, Tx = 0 });
	}

	_debug_panels = AddPanels($"@{SCRIPT_ID}DebugDisplay");
	_warning_panels = AddPanels($"@{SCRIPT_ID}WarningDisplay");

	FindPanels();

	if (!Me.CustomName.Contains(Program.SCRIPT_SHORT_NAME)) {
	    // Update our block to include our script name
	    Me.CustomName = $"{Me.CustomName} - {Program.SCRIPT_SHORT_NAME}";
	}
	Log(SCRIPT_TITLE);
    }

    public void Main(string argument, UpdateType updateSource) {
	try {
	    // Tally up all invocation times and record them as one on the non-command runs.
	    if (_last_run != LAST_RUN_NONE) {
		_tallies[_last_run].Time += Prog.Runtime.LastRunTimeMs;
	    }
	    _last_run = LAST_RUN_NONE;
	    if ((updateSource & UpdateType.Update100) != 0) {
                //Warning("Issuing times");
		_last_run = LAST_RUN_MAIN;
		_cycles++;

		IssueDatapoint(CHART_TIME, TimeAsUsec(_tallies[LAST_RUN_MAIN].Time + _tallies[LAST_RUN_EVENT].Time));
		IssueDatapoint(CHART_MAIN_TIME, TimeAsUsec(_tallies[LAST_RUN_MAIN].Time));
		IssueDatapoint(CHART_EVENTS_TIME, TimeAsUsec(_tallies[LAST_RUN_EVENT].Time));
		if (_cycles > 1) {
		    _time_total += _tallies[LAST_RUN_MAIN].Time + _tallies[LAST_RUN_EVENT].Time;
		    if (_cycles == 201) {
			Warning($"Total time after 200 cycles: {_time_total}ms.");
		    }
		}
		_tallies[LAST_RUN_MAIN].Time = 0.0;
		_tallies[LAST_RUN_EVENT].Time = 0.0;

		ClearPanels(_debug_panels);

		Log(SCRIPT_TITLE_NL);

		if ((_cycles % 30) == 1) {
                    //Warning("Looking for updates");
		    FindPanels();
		    CreateDataset(CHART_TIME, "us");
		    CreateDataset(CHART_MAIN_TIME, "us");
		    CreateDataset(CHART_EVENTS_TIME, "us");
		    CreateDataset(CHART_LOAD, "%");
		    CreateDataset(CHART_EVENTS_RX, "");
		    CreateDataset(CHART_EVENTS_TX, "");
		    CreateDataset(CHART_EVENTS_BANDWIDTH, "");
		}
	    }

	    if ((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) != 0) {
                //Warning("Running mainloop");
		_last_run = LAST_RUN_MAIN;
		if (_mainloop_handler != null) {
		    _mainloop_handler(updateSource);
		}
	    }

	    if ((updateSource & UpdateType.Update100) != 0) {
                //Warning("Issuing load/rx/tx/bandwidth");
		double load = (double)(_tallies[LAST_RUN_MAIN].Instr + _tallies[LAST_RUN_EVENT].Instr) * 100.0 / (double)(Prog.Runtime.MaxInstructionCount * (_tallies[LAST_RUN_MAIN].Cycles + _tallies[LAST_RUN_EVENT].Cycles));
		_tallies[LAST_RUN_MAIN].Instr = 0;
		_tallies[LAST_RUN_EVENT].Instr = 0;
		IssueDatapoint(CHART_LOAD, load);

		// Slightly sneaky, push the counts for sending rx/tx themseles onto the next cycle.
		int rx = _tallies[LAST_RUN_MAIN].Rx + _tallies[LAST_RUN_EVENT].Rx, tx = _tallies[LAST_RUN_MAIN].Tx + _tallies[LAST_RUN_EVENT].Tx;
		_tallies[LAST_RUN_MAIN].Rx = 0;
		_tallies[LAST_RUN_EVENT].Rx = 0;
		_tallies[LAST_RUN_MAIN].Tx = 0;
		_tallies[LAST_RUN_EVENT].Tx = 0;

		IssueDatapoint(CHART_EVENTS_RX, (double)rx);
		IssueDatapoint(CHART_EVENTS_TX, (double)tx);

		Log($"[Cycle {_cycles}]\n  Main loops: {_tallies[LAST_RUN_MAIN].Cycles}. Event loops: {_tallies[LAST_RUN_EVENT].Cycles}\n  Events: {rx} received, {tx} transmitted.\n  {_subscriptions.Count()} event listeners.");
                Log($"  Max event bandwidth: {_max_event.Utilization}% ({_max_event.Rx} of {_max_event.Max} on '{_max_event.Channel}').");
		FlushToPanels(_debug_panels);

		_tallies[LAST_RUN_MAIN].Cycles = 0;
		_tallies[LAST_RUN_EVENT].Cycles = 0;
                double utilization = _max_event.Utilization;
                _max_event.Utilization = 0.0;
                _max_event.Rx = 0;
                _max_event.Max = 0;
                _max_event.Channel = "";

		IssueDatapoint(CHART_EVENTS_BANDWIDTH, utilization);
	    }

	    if ((updateSource & UpdateType.IGC) != 0) {
                //Warning("Consuming events");
		_last_run = LAST_RUN_EVENT;
		ZIPubSubSubscription subscription;
		string event_name = argument;
		if (_subscriptions.TryGetValue(event_name, out subscription)) {
                    int event_rx = 0;
		    while (subscription.Listener.HasPendingMessage) {
			MyIGCMessage message = subscription.Listener.AcceptMessage();
			_tallies[_last_run].Rx++;
                        event_rx++;
			subscription.Handler(event_name, message.Data);
		    }
                    double event_utilization = (double)event_rx * 100.0 / (double)subscription.Listener.MaxWaitingMessages;
                    if (event_utilization > _max_event.Utilization) {
                        _max_event.Utilization = event_utilization;
                        _max_event.Rx = event_rx;
                        _max_event.Max = subscription.Listener.MaxWaitingMessages;
                        _max_event.Channel = event_name;
                    }
		}
	    } else if (argument != null && argument != "") {
                //Warning("Processing command");
		ProcessCommand(argument);
	    }
	    if (_last_run != LAST_RUN_NONE) {
		_tallies[_last_run].Cycles++;
		_tallies[_last_run].Instr += Prog.Runtime.CurrentInstructionCount;
	    }
	} catch (Exception e) {
	    string mess = $"An exception occurred during script execution.\nException: {e}\n---";
	    Log(mess);
	    Warning(mess);
	    FlushToPanels(_debug_panels);
	    throw;
	}
    }

    public void AddCommand(string command, Action<string, MyCommandLine, string> handler) {
	_commands[command] = handler;
    }

    public void RemoveCommand(string command) {
	_commands.Remove(command);
    }

    public void ProcessCommand(string argument) {
	if (_command_line.TryParse(argument)) {
	    string command = _command_line.Argument(0);
	    Action<string, MyCommandLine, string> handler;
	    if (command == null) {
		Warning("No command specified");
	    } else if (_commands.TryGetValue(command, out handler)) {
		handler(command, _command_line, argument);
	    } else {
		Warning($"Unknown command {command}");
	    }
	} else {
	    Warning($"Unable to parse command: {argument}");
	}
    }

    public void Subscribe(string event_name, Action<string, object> handler) {
	if (!_subscriptions.ContainsKey(event_name)) {
	    IMyBroadcastListener listener;
	    listener = Prog.IGC.RegisterBroadcastListener($"{PUBSUB_BROADCAST_PREFIX} {event_name}");
	    listener.SetMessageCallback(event_name);
	    _subscriptions[event_name] = new ZIPubSubSubscription(listener, handler);
	}
    }

    public void Unsubscribe(string event_name) {
	ZIPubSubSubscription subscription;
	if (_subscriptions.TryGetValue(event_name, out subscription)) {
	    Prog.IGC.DisableBroadcastListener(subscription.Listener);
	    _subscriptions.Remove(event_name);
	}
    }

    // FIXME: including sender was good.
    public void PublishEvent<TData>(string event_name, TData event_args, TransmissionDistance distance = TransmissionDistance.CurrentConstruct, bool send_to_self = false) {
	_tallies[_last_run].Tx++;
	Prog.IGC.SendBroadcastMessage($"{PUBSUB_BROADCAST_PREFIX} {event_name}", event_args, distance);
	if (send_to_self) {
	    ZIPubSubSubscription subscription;
	    if (_subscriptions.TryGetValue(event_name, out subscription)) {
		_tallies[_last_run].Rx++;
		subscription.Handler(event_name, event_args);
	    }
	}
    }

    public void CreateDataset(string chart_name, string unit) {
	PublishEvent("dataset.create", new MyTuple<string, string>(chart_name, unit), send_to_self: _send_data_events_to_self);
    }

    public void IssueDatapoint(string chart_name, double value) {
	//Warning($"IssueDatapoint {chart_name}={value}");
	PublishEvent($"datapoint.issue.{chart_name}", new MyTuple<string, double>(chart_name, value), send_to_self: _send_data_events_to_self);
    }

    public double TimeAsUsec(double t) {
	//return (t * 1000.) / TimeSpan.TicksPerMillisecond;
	return t * 1000.0;
    }

    public int AddPanels(string tag) {
	int id = _panels.Count;
	_panels.Add(new List<IMyTextPanel>());
	_panel_tags.Add(tag);
	_panel_text.Add("");
	return id;
    }

    public void FindPanels() {
	for (int i = 0; i < _panels.Count; i++) {
	    _panels[i].Clear();
	    Prog.GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(_panels[i], block => block.CustomName.Contains(_panel_tags[i]) && block.IsSameConstructAs(Me));
	    for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
		_panels[i][j].ContentType = ContentType.TEXT_AND_IMAGE;
		_panels[i][j].Font = "Monospace";
		_panels[i][j].FontSize = 0.5F;
		_panels[i][j].TextPadding = 0.5F;
		_panels[i][j].Alignment = TextAlignment.LEFT;
	    }
	}
    }

    public void ClearAllPanels() {
	for (int i = 0; i < _panels.Count; i++) {
	    ClearPanels(i);
	}
    }

    public void ClearPanels(int kind) {
	_panel_text[kind] = "";
    }

    public void WritePanels(int kind, string s) {
	_panel_text[kind] += s;
    }

    public void PrependPanels(int kind, string s) {
	_panel_text[kind] = s + _panel_text[kind];
    }

    public void FlushToAllPanels() {
	for (int i = 0; i < _panels.Count; i++) {
	    FlushToPanels(i);
	}
    }

    public void FlushToPanels(int kind) {
	for (int i = 0, sz = _panels[kind].Count; i < sz; i++) {
	    if (_panels[kind][i] != null) {
		_panels[kind][i].WriteText(_panel_text[kind], false);
	    }
	}
    }

    public void Log(string s) {
	WritePanels(_debug_panels, s + "\n");
	Echo(s);
    }

    public void Warning(string s) {
	// Never clear buffer and and always immediately flush.
	// Prepend because long text will have the bottom hidden.
	PrependPanels(_warning_panels, $"[{DateTime.Now,11:HH:mm:ss.ff}] {s}\n");
	FlushToPanels(_warning_panels);
    }
}
