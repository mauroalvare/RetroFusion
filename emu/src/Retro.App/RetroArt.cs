using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>Original front-panel, circuit and instrument artwork, drawn at the current UI scale.</summary>
internal static class RetroArt
{
    private static uint Ink => ImGui.GetColorU32(ImGuiCol.Text);
    private static uint Copper => ImGui.ColorConvertFloat4ToU32(UiTheme.Accent);
    public static void Activity()
    {
        var at=ImGui.GetCursorScreenPos();var draw=ImGui.GetWindowDrawList();
        float phase=(float)ImGui.GetTime()*MathF.PI/0.9f;
        for(int i=0;i<3;i++)
        {
            float glow=UiTheme.HighContrast?1:0.45f+0.55f*(0.5f+0.5f*MathF.Sin(phase-i*1.5f));
            var color=UiTheme.Accent;color.W=glow;
            draw.AddCircleFilled(at+UiTheme.Px(4+i*8,10),UiTheme.Px(2),ImGui.ColorConvertFloat4ToU32(color),8);
        }
        ImGui.Dummy(UiTheme.Px(26,20));ImGui.SameLine();
    }

    public static void Screw(ImDrawListPtr draw,Vector2 center)
    {
        uint rim=ImGui.GetColorU32(ImGuiCol.Border);
        draw.AddCircle(center,UiTheme.Px(2.5f),rim,8,1);
        draw.AddLine(center-UiTheme.Px(1.5f,0),center+UiTheme.Px(1.5f,0),rim,1);
    }

    public static void Brand()
    {
        var p=ImGui.GetCursorScreenPos(); var draw=ImGui.GetWindowDrawList();
        ImGui.Dummy(UiTheme.Px(164,30));
        Chip(draw,p+UiTheme.Px(1,1),UiTheme.Px(28));
        using(UiTheme.Display()) draw.AddText(p+UiTheme.Px(38,1),Ink,UiText.Get("RETROEMU"));
    }

    private static void Chip(ImDrawListPtr draw,Vector2 origin,float size)
    {
        float unit=size/28;
        Vector2 P(float x,float y)=>origin+new Vector2(x,y)*unit;
        draw.AddRectFilled(P(4,4),P(24,24),ImGui.GetColorU32(ImGuiCol.FrameBg),unit);
        draw.AddRect(P(4,4),P(24,24),Copper,unit,ImDrawFlags.None,unit);
        for(int pin=0;pin<4;pin++)
        {
            float at=7+pin*4;
            draw.AddLine(P(0,at),P(4,at),Copper,unit);draw.AddLine(P(24,at),P(28,at),Copper,unit);
            draw.AddLine(P(at,0),P(at,4),Copper,unit);draw.AddLine(P(at,24),P(at,28),Copper,unit);
        }
        string[] re=["1100111","1010100","1100110","1010100","1010111"];
        for(int y=0;y<re.Length;y++)for(int x=0;x<re[y].Length;x++)
            if(re[y][x]=='1')draw.AddRectFilled(P(7+x*2,9+y*2),P(8.5f+x*2,10.5f+y*2),Ink);
    }

    public static bool ModeButton(string label,bool active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button,active?ImGui.GetStyle().Colors[(int)ImGuiCol.Header]:UiTheme.Panel);
        bool clicked=ImGui.Button(UiText.Label(label));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var draw=ImGui.GetWindowDrawList();
        if(active)draw.AddRectFilled(new(min.X+UiTheme.Px(5),max.Y-UiTheme.Px(3)),new(max.X-UiTheme.Px(5),max.Y-UiTheme.Px(1)),Copper);
        return clicked;
    }

    public static void LibraryMasthead(int machines,int originals)
    {
        var origin=ImGui.GetCursorScreenPos();float width=ImGui.GetContentRegionAvail().X;
        bool art=width>=UiTheme.Px(780);
        if(art && !UiTheme.HighContrast && !UiTheme.Terminal) FrontPanel(ImGui.GetWindowDrawList(),origin+new Vector2(width-UiTheme.Px(305),0),UiTheme.Px(300,94),originals);
        UiTheme.Eyebrow("ARCHIVO / 8 Y 16 BITS");
        UiTheme.Heading("Encendé una máquina.",UiText.Format($"{machines} sistemas · {originals} archivos originales en esta carpeta."));
        if(art)
        {
            float space=origin.Y+UiTheme.Px(96)-ImGui.GetCursorScreenPos().Y;
            if(space>0)ImGui.Dummy(new(0,space));
        }
    }

    private static void FrontPanel(ImDrawListPtr draw,Vector2 origin,Vector2 size,int value)
    {
        Vector2 P(float x,float y)=>origin+new Vector2(x/300*size.X,y/94*size.Y);
        uint plate=0xFFB6C5CE,dark=0xFF273129,line=0xFF7E918A,green=0xFF9CCA9B;
        draw.AddRectFilled(P(0,4),P(300,94),0x60000000,UiTheme.Px(2));
        draw.AddRectFilled(P(0,0),P(300,90),plate,UiTheme.Px(2));
        draw.AddRect(P(3,3),P(297,87),line,UiTheme.Px(1));
        for(int i=0;i<4;i++)
        {
            var at=P(i%2==0?9:291,i<2?9:81);
            draw.AddCircle(at,UiTheme.Px(2),line,8,1);draw.AddLine(at-UiTheme.Px(1,0),at+UiTheme.Px(1,0),dark,1);
        }
        draw.AddRectFilled(P(18,17),P(174,72),dark,UiTheme.Px(1));
        for(int x=25;x<170;x+=12)draw.AddLine(P(x,22),P(x,67),0xFF394E3C,1);
        for(int y=25;y<69;y+=10)draw.AddLine(P(23,y),P(169,y),0xFF394E3C,1);
        Vector2[] wave=[P(23,53),P(39,53),P(39,34),P(64,34),P(64,53),P(91,53),P(91,34),P(115,34),P(115,53),P(145,53),P(145,34),P(170,34)];
        for(int i=1;i<wave.Length;i++)draw.AddLine(wave[i-1],wave[i],green,UiTheme.Px(1.5f));
        for(int i=0;i<8;i++)
        {
            uint color=(value&(1<<i))!=0?0xFF55B7E0:0xFF5A6555;
            draw.AddCircleFilled(P(194+i%4*24,25+i/4*16),UiTheme.Px(3),color,10);
        }
        for(int i=0;i<4;i++)
        {
            draw.AddRectFilled(P(188+i*24,54),P(202+i*24,69),dark,UiTheme.Px(1));
            draw.AddRectFilled(P(190+i*24,56),P(200+i*24,61),line);
        }
        draw.AddLine(P(18,80),P(282,80),line,1);
    }

    public static void Tool(ImDrawListPtr draw,string kind,Vector2 origin,float size)
    {
        Vector2 P(float x,float y)=>origin+new Vector2(x,y)*(size/48);
        void Line(float x,float y,float xx,float yy)=>draw.AddLine(P(x,y),P(xx,yy),Copper,UiTheme.Px(1.3f));
        void Box(float x,float y,float w,float h)=>draw.AddRect(P(x,y),P(x+w,y+h),Copper,UiTheme.Px(1),ImDrawFlags.None,UiTheme.Px(1.3f));
        switch(kind)
        {
            case "new": case "code":
                Chip(draw,origin+new Vector2(size*0.13f),size*0.74f);break;
            case "fusion":
                Box(2,4,15,19);Box(31,4,15,19);Box(15,31,18,13);
                Line(9,23,9,27);Line(39,23,39,27);Line(9,27,24,27);Line(39,27,24,27);Line(24,27,24,31);break;
            case "inspired":
                Box(4,14,18,24);Line(9,20,17,20);Line(9,26,17,26);Line(9,32,14,32);
                Line(26,26,32,26);Line(29,23,32,26);Line(29,29,32,26);
                draw.AddCircle(P(39,26),size*0.13f,Copper,16,UiTheme.Px(1.3f));Line(39,12,39,16);Line(39,36,39,40);Line(47,26,50,26);break;
            case "sound":
                Box(6,17,9,14);Line(15,17,26,8);Line(26,8,26,40);Line(26,40,15,31);
                Line(33,16,38,21);Line(38,21,38,27);Line(38,27,33,32);break;
            case "preferences":
                for(int i=0;i<3;i++){float x=9+i*15;Line(x,5,x,43);Box(x-4,12+i*7,8,6);}break;
            case "capture":
                Box(4,13,40,28);Box(10,7,12,6);draw.AddCircle(P(26,27),size*0.14f,Copper,16,UiTheme.Px(1.3f));break;
            case "slots": case "replay-tools":
                Box(5,6,38,36);draw.AddCircle(P(16,24),size*0.12f,Copper,16,UiTheme.Px(1.3f));
                draw.AddCircle(P(32,24),size*0.12f,Copper,16,UiTheme.Px(1.3f));Line(15,33,33,33);Line(14,12,34,12);break;
            case "modify": case "debugger":
                Box(4,7,40,30);Line(18,43,30,43);Line(24,37,24,43);
                Line(9,27,16,27);Line(16,27,20,16);Line(20,16,26,30);Line(26,30,30,23);Line(30,23,39,23);break;
            default:
                Box(4,7,40,34);Line(10,16,22,16);Line(10,24,35,24);Line(10,32,29,32);
                draw.AddCircleFilled(P(36,14),UiTheme.Px(2),Copper,10);break;
        }
    }
}
