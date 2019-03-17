
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;



namespace C0_Compiler
{
    internal enum AccentState
    {
        ACCENT_DISABLED = 1,
        ACCENT_ENABLE_GRADIENT = 0,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_INVALID_STATE = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        // ...
        WCA_ACCENT_POLICY = 19
        // ...
    }
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        string file;//输入文件路径
        string source; //文件输入内容
        bool hasError;//是否检查到错误
        private string TOKEN, last_TOKEN;//当前符号，上一个符号
        private int current_pos, line_pos,sizeOfFile,last_pos;//当前代码位置,行位置,文件大小,上一个代码位置
        static private char Char;//当前字符
        int line_num,last_line_num; //行数
        int int_num;//整数值
        bool hasreturn;//函数是否已返回
        double float_num;//小数值
        int current_adr;//当前层的相对地址位置
        List<int> data_stack;//数据栈
        int register_code,register_base,register_top;//代码寄存器，基地址寄存器，栈顶寄存器
        int mid_base;//基地址中间值
        //Pcode枚举
        public enum Pcode {LOD = 1, LDC, LDA ,STO, MUS, ADD, SUB, MUL, DIV, JPC, JMP, LSS, LER, GRT, GEQ, EQL, NEQ, MKS, CAL, RED,WRS, WRW, EXF}
        //符号类型枚举
        public enum SY { CONSTSY = 1, INTSY, VOIDSY, IFSY, ELSESY, WHILESY, MAINSY, RETURNSY, PRINTFSY, SCANFSY, IDSY, INTNUMSY, FLOATNUMSY, STRSY, PLUSSY, MINUSSY, STARSY, DIVISY, LESSSY, LESSEQUSY, GREATERSY, GREATEREQUSY, NOTEQUSY, EQUSY, LPARSY, RPARSY, LBRACESY, RBRACESY, COMMASY, SEMISY, ASSIGNSY };
        private SY symbol, last_symbol;//当前符号，上一个保存的符号    
        string currentFuntion_name;//当前参数,局部,常量，变量所在的函数名

        [DllImport("Kernel32.dll")]
        public static extern bool FreeConsole();
        [DllImport("Kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("User32.dll ", EntryPoint = "FindWindow")]
        private static extern int FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll ", EntryPoint = "GetSystemMenu")]
        extern static IntPtr GetSystemMenu(IntPtr hWnd, IntPtr bRevert);

        [DllImport("user32.dll ", EntryPoint = "RemoveMenu")]
        extern static int RemoveMenu(IntPtr hMenu, int nPos, int flags);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("Kernel32.dll")]
        private static extern int GetConsoleOutputCP();
        //当前是否有控制台
        public static bool HasConsole
        {
            get { return GetConsoleWindow() != IntPtr.Zero; }
        }
        //控制台输入输出重定向
        static void InvalidateOutAndError()
        {
            Type type = typeof(System.Console);

            System.Reflection.FieldInfo _out = type.GetField("_out",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);


            System.Reflection.FieldInfo _in = type.GetField("_in",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            System.Reflection.FieldInfo _error = type.GetField("_error",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            System.Reflection.MethodInfo _InitializeStdOutError = type.GetMethod("InitializeStdOutError",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Debug.Assert(_out != null);
            Debug.Assert(_error != null);
            Debug.Assert(_in != null);
            Debug.Assert(_InitializeStdOutError != null);

            _out.SetValue(null, null);
            _in.SetValue(null, null);
            _error.SetValue(null, null);

            _InitializeStdOutError.Invoke(null, new object[] { true });
        }


        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        //pcode类
        public class code
        {
            public Pcode mnemonic;
            public int lev;
            public int val;

            public code(Pcode mnemonic, int lev, int val)
            {
                this.mnemonic = mnemonic;
                this.lev = lev;
                this.val = val;
            }
        }
        //产生的pcode列表
        List<code> codes;
        class symbol_Item//符号表项
        {
            public string name;//符号名
            public int kind;//1:变量/2:常量/3:函数
            public SY type; //变量的类型/函数的返回类型
            public int value;//常量值
            public int lev;//层次
            public int addr;//地址
            public List<symbol_Item> local_var;//函数的参数和局部常量,变量
            public symbol_Item(){

            }
            public symbol_Item(string name, int kind, SY type, int value, int lev, int addr)
            {
                this.name = name;
                this.kind = kind;
                this.type = type;
                this.value = value;
                this.lev = lev;
                this.addr = addr;
                if (kind == 3)
                    this.local_var = new List<symbol_Item>();
            }
        }
        List<symbol_Item> symbol_table;
        //搜索符号表
        bool search_symbol(string Name, bool isassign, out symbol_Item symbol_Item)
        {         
            if (currentFuntion_name == string.Empty)
            {
                foreach (symbol_Item si in symbol_table)
                    if (si.name == Name)
                    {
                        symbol_Item = si;
                        return true;
                    }
                symbol_Item = new symbol_Item();
                return false;
            }
            else
            {
                foreach (symbol_Item si in symbol_table)
                    if (si.name == currentFuntion_name)
                    {
                        if (si.local_var != null)
                        {
                            foreach (symbol_Item subsi in si.local_var)
                                if (subsi.name == Name)
                                {
                                    symbol_Item = subsi;
                                    return true;
                                }
                        }
                        symbol_Item = new symbol_Item();
                    }
                if (!isassign)
                    foreach (symbol_Item si in symbol_table)
                        if (si.name == Name)
                        {
                            symbol_Item = si;
                            return true;
                        }
                symbol_Item = new symbol_Item();
                return false;
            }
        }
        //添加符号表
        void add_symbol(string Name, int Kind, SY type, int value = 0)
        {
            symbol_Item si;
            
            if (search_symbol(Name, true, out si))
                error(11);//该符号已存在,声明失败
            int addr;
            if (Kind == 3)
                addr = codes.Count;
            else if (Kind == 1)
                addr = current_adr++;
            else
                addr = 0;
                
            if (currentFuntion_name == string.Empty)
            {
                symbol_table.Add(new symbol_Item(Name, Kind, type, value, 0, addr));
            }
            else
                for(int i = 0; i < symbol_table.Count; i++)
                    if(symbol_table[i].name == currentFuntion_name)
                    {
                        symbol_table[i].local_var.Add(new symbol_Item(Name, Kind, type, value, 1, addr));
                        break;
                    }
            if (Kind == 3)
            {
                currentFuntion_name = Name;
                current_adr = 5;
            }

        }
        //目标代码生成
        void gene_code(Pcode mnemonic, int lev = 0, int val = 0)
        {
            codes.Add(new code(mnemonic, lev, val));
        }
        //主界面初始化
        public MainWindow()
        {
            FreeConsole();           
            InitializeComponent();
        }
        //界面加载处理
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnableBlur();         
        }
        //开启背景模糊
        internal void EnableBlur()
        {
           var windowHelper = new WindowInteropHelper(this);

            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
        //界面拖动
        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

            DragMove();
        }

       

        //文件输入的按钮点击
        private void Button_Click(object sender, RoutedEventArgs e)
        {
           FreeConsole();
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = false;//该值确定是否可以选择多个文件
            dialog.Title = "请选择文件夹";
            dialog.Filter = "所有文件(*.*)|*.*";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                file = dialog.FileName;
                source = File.ReadAllText(@file);
                source += '\n';
                text_in.Text = string.Empty;
                text_out.Text = string.Empty;
                text_error.Text = string.Empty;
                sizeOfFile = source.Length;
                current_pos = 0;
                line_num = 1;
                line_pos = 0;
                Thread thread = new Thread(new ThreadStart(start));
                thread.Start();
            }
        }
        //当前目标代码运行处理
        void deal_code()
        {
            int x = codes[register_code].lev, y = codes[register_code].val;
            int a, b;
            switch (codes[register_code].mnemonic)
            {
                case Pcode.LOD:
                    if (x == 1)
                        stack_push(data_stack[register_base + y]);
                    else
                        stack_push(data_stack[y]);
                    register_code++;
                    break;
                case Pcode.LDA:
                    if (x == 1)
                        stack_push(register_base + y);
                    else
                        stack_push(y);
                    register_code++;
                    break;
                case Pcode.JMP:
                    register_code = y;
                    break;
                case Pcode.JPC:
                    int z = stack_pop();
                    if (z > 0)
                        register_code++;
                    else
                        register_code = y;
                    break;
                case Pcode.MKS:
                    stack_push(0);
                    stack_push(0);
                    stack_push(0);
                    stack_push(register_base);
                    stack_push(y);
                   // Console.WriteLine("yuanbase::::"+register_base);
                    mid_base = register_top - 4;
                    register_code++;
                    break;
                case Pcode.CAL:
                    register_base = mid_base;
                    if (symbol_table[data_stack[register_base + 4]].local_var != null)
                        for (int i = 0; i < symbol_table[data_stack[register_base + 4]].local_var.Count - symbol_table[data_stack[register_base + 4]].value; i++)
                            stack_push(0);
                    data_stack[register_base + 1] = register_code + 1;
                    register_code = y;
                    break;
                case Pcode.LDC:
                    stack_push(y);
                    register_code++;
                    break;
                case Pcode.RED:
                    try
                    {
                        data_stack[stack_pop()] = Convert.ToInt32(Console.ReadLine());
                    }
                    catch
                    {
                        error(1,true);
                    }
                    register_code++;
                    break;
                case Pcode.WRS:
                    Console.Write((char)y);
                    register_code++;
                    break;
                case Pcode.WRW:
                    Console.Write(stack_pop());
                    register_code++;
                    break;
                case Pcode.ADD:
                    b = stack_pop();
                    a = stack_pop();
                    stack_push(a + b);
                    register_code++;
                    break;
                case Pcode.SUB:
                    b = stack_pop();
                    a = stack_pop();
                    stack_push(a - b);
                    register_code++;
                    break;
                case Pcode.MUL:
                    b = stack_pop();
                    a = stack_pop();
                    stack_push(a * b);
                    register_code++;
                    break;
                case Pcode.DIV:
                    b = stack_pop();
                    a = stack_pop();
                    if (b == 0)
                        error(2, true);
                    else
                        stack_push(a / b);
                    register_code++;
                    break;
                case Pcode.EQL:
                    b = stack_pop();
                    a = stack_pop();
                    if (a == b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.MUS:
                    stack_push(-1*stack_pop());
                    register_code++;
                    break;
                case Pcode.GEQ:
                    b = stack_pop();
                    a = stack_pop();
                    if (a >= b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.GRT:
                    b = stack_pop();
                    a = stack_pop();
                    if (a > b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.LER:
                    b = stack_pop();
                    a = stack_pop();
                    if (a <= b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.LSS:
                    b = stack_pop();
                    a = stack_pop();
                    if (a < b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.NEQ:
                    b = stack_pop();
                    a = stack_pop();
                    if (a != b)
                        stack_push(1);
                    else
                        stack_push(0);
                    register_code++;
                    break;
                case Pcode.STO:
                    b = stack_pop();
                    a = stack_pop();
                    data_stack[a] = b;
                    register_code++;
                    break;
                case Pcode.EXF:
                    register_code = data_stack[register_base + 1];
                    a = data_stack[register_base + 3];
                    if (symbol_table[data_stack[register_base + 4]].type == SY.VOIDSY)
                    {
                        while (register_top >= register_base)
                            stack_pop();
                        register_base = a;
                    }
                    else
                    {                     
                        data_stack[register_base] = stack_pop();
                        while (register_top > register_base)
                            stack_pop();
                        register_base = a;
                    }
                    break;
            }
        }
        //压栈
        void stack_push(int num)
        {
            data_stack.Add(num);
            register_top++;
            //Console.WriteLine("\t\t\tpush" + data_stack[register_top]);
            //Console.WriteLine("top" + register_top);
        }
        //出栈
        int stack_pop()
        {
            int num = data_stack[register_top];
            data_stack.RemoveAt(register_top--);
            //Console.WriteLine("\t\t\tpop" + num);
            //Console.WriteLine("top" + register_top);
            return num;
        }
        //运行目标代码
        void run_code()
        {
            AllocConsole();
            InvalidateOutAndError();
            Console.Title = file;
            string fullPath = System.Environment.CurrentDirectory + "\\C0_Compiler.exe";
            //根据控制台标题找控制台
            int WINDOW_HANDLER = FindWindow(null, fullPath);
            //找关闭按钮
            IntPtr CLOSE_MENU = GetSystemMenu((IntPtr)WINDOW_HANDLER, IntPtr.Zero);
            int SC_CLOSE = 0xF060;
            //关闭按钮禁用
            RemoveMenu(CLOSE_MENU, SC_CLOSE, 0x0);
            symbol_Item si;
            search_symbol("main",false,out si);
            this.register_code = si.addr;
            register_top = -1;
            register_base = 0;
            data_stack = new List<int>();
            stack_push(0);
            stack_push(0);
            stack_push(0);
            stack_push(-1);
            stack_push(symbol_table.Count);
            for (int i = 0; i < symbol_table.Count; i++)
                if (symbol_table[i].kind == 1)
                    stack_push(0);
            stack_push(0);
            stack_push(codes.Count);
            stack_push(0);
            stack_push(register_base);
            stack_push(symbol_table.IndexOf(si));
            register_base = register_top - 4;
            if (si.local_var != null)
                for (int i = 0; i < si.local_var.Count; i++)
                    stack_push(0);
            while (this.register_code < codes.Count)
                try
                { deal_code(); }
                catch
                {
                   // Console.WriteLine("error "+register_code);

                    //Console.WriteLine("base " + register_base);
                }
            try
            {
                Console.WriteLine("\n输入回车退出");
                Console.ReadLine();
            }
            catch
            { }
            FreeConsole();
            
        }
        //输出目标代码
        private void print_pcode()
        {
            for (int i = 0; i < codes.Count; i++)
            {
                string x = null, y = null;
                switch (codes[i].mnemonic)
                {
                    case Pcode.LOD:                       
                    case Pcode.LDA:
                        x = codes[i].lev.ToString();
                        y = codes[i].val.ToString();
                        break;
                    case Pcode.JMP:
                    case Pcode.JPC:
                    case Pcode.MKS:
                    case Pcode.CAL:
                    case Pcode.LDC:
                    case Pcode.RED:
                    case Pcode.WRS:
                    case Pcode.WRW:
                        x = " ";
                        y = codes[i].val.ToString();
                        break;
                    case Pcode.ADD:
                    case Pcode.SUB:
                    case Pcode.MUL:
                    case Pcode.DIV:
                    case Pcode.EQL:
                    case Pcode.MUS:
                    case Pcode.GEQ:
                    case Pcode.GRT:
                    case Pcode.LER:
                    case Pcode.LSS:                                      
                    case Pcode.NEQ:
                    case Pcode.STO:
                    case Pcode.EXF:
                        break;
                    
                }
                text_out.Dispatcher.Invoke(new MethodInvoker(delegate
                {
                    text_out.Text += string.Format("{0,-4}", i + 1) + string.Format("{0,-8}", codes[i].mnemonic) + x + '\t' + y + "\n";
                }));
            }
        }
        //开始编译
        private void start()
        {
            symbol_table = new List<symbol_Item>();
            codes = new List<code>();
            currentFuntion_name = string.Empty;
            current_adr = 5;
            hasError = false;
            getsym();
            state_program();
            while (current_pos < sizeOfFile)
                getsym();
            print_pcode();
            if(!hasError)
                run_code();
        }
        void save_condition()//保存当前的词法分析状态
        {
            last_TOKEN = TOKEN;
            last_line_num = line_num;
            last_pos = current_pos;
            last_symbol = symbol;
        }

        void recover_condition()//恢复上一个保存的词法分析状态
        {
            TOKEN = last_TOKEN;
            line_num = last_line_num;
            current_pos = last_pos;
            symbol = last_symbol;
        }

        void state_program()//<程序>分析
        {
            if (symbol == SY.CONSTSY)
                state_const();
            if (symbol != SY.INTSY && symbol != SY.VOIDSY)
            {
                error(20);
                while (symbol != SY.INTSY && symbol != SY.VOIDSY)
                    getsym();
            }
            if (symbol == SY.INTSY)
            {
                save_condition();
                getsym();
                if (symbol == SY.IDSY)
                {
                    getsym();
                    if (symbol == SY.LPARSY)
                        recover_condition();
                    else
                    {
                        recover_condition();
                        state_var();
                    }
                }
                else
                    error(9);//应为标识符
            }
            if (symbol != SY.INTSY && symbol != SY.VOIDSY)
            {
                error(20);
                while (symbol != SY.INTSY && symbol != SY.VOIDSY)
                {
                    getsym();
                }
            }

            while (symbol == SY.INTSY || symbol == SY.VOIDSY)
            {
                save_condition();
                getsym();
                if (symbol == SY.MAINSY)
                {
                    recover_condition();
                    break;
                }
                else
                {
                    recover_condition();
                    define_function();
                }
            }
            while (symbol != SY.INTSY && symbol != SY.VOIDSY)
                getsym();
            main_function();
        }
        //＜常量说明部分＞
        void state_const()
        {
            if (symbol == SY.CONSTSY)
            {
                do
                {
                    getsym();
                    define_const();
                } while (symbol == SY.COMMASY);
                if (symbol == SY.SEMISY)
                    getsym();
                else
                    error(4);//缺少";"
            }
        }//＜常量定义＞
        void define_const()
        {
            if (symbol != SY.IDSY)
                error(9);//const后应为标识符
            string s = TOKEN;
            getsym();           
            if (symbol == SY.ASSIGNSY)
            {
                getsym();
                if (symbol == SY.INTNUMSY)
                    add_symbol(s, 2, SY.INTNUMSY, int_num);
                else
                    error(233);//=后应为整数
            }
            else
                error(13);//标识符后应为=
            getsym();
        }
        void state_head(int kind)//＜声明头部＞
        {
            if (symbol == SY.INTSY)
            {
                getsym();
                if (symbol == SY.IDSY)
                {
                    add_symbol(TOKEN, kind, SY.INTNUMSY);
                    getsym();
                }
                else
                    error(9);//int后应为标识符
            }
            
        }
        void state_var()//＜变量说明部分＞
        {
            state_head(1);
            while(symbol == SY.COMMASY)
            {
                getsym();
                if (symbol == SY.IDSY)
                {
                    add_symbol(TOKEN, 1, SY.INTNUMSY);
                    getsym();
                }
                else
                    error(9);//应为标识符    
            }
            if (symbol == SY.SEMISY)
                getsym();
            else
                error(4);//缺少";"
        }
        void define_function()//＜函数定义部分＞
        {
            if (symbol == SY.INTSY)
            {
                hasreturn = false;
                state_head(3);
            }
            else if (symbol == SY.VOIDSY)
            {
                hasreturn = true;
                getsym();
                if (symbol == SY.IDSY)
                {
                    add_symbol(TOKEN, 3, SY.VOIDSY);
                    getsym();
                }
                else
                    error(9);//"void"后应为标识符
            }
            else
                error(14);//函数缺少返回值类型声明
            parameter();
            state_compound();
            if (!hasreturn)
                error(22);
            currentFuntion_name = string.Empty;
            current_adr = 5;
        }
        void state_compound()//＜复合语句＞
        {
            if (symbol == SY.LBRACESY)
            {
                getsym();
            }
            else
                error(8);//应为"{"
            if (symbol == SY.CONSTSY)
                state_const();
            if (symbol == SY.INTSY)
                state_var();
            state_sequence();
            if (symbol == SY.RBRACESY)
                getsym();
            else
                error(6);//缺少"}"
        }
        void parameter()//＜参数＞
        {
            if (symbol == SY.LPARSY)
                getsym();
            else
                error(7);//应为"("
            parameter_table();
            if (symbol == SY.RPARSY)
                getsym();
            else
                error(5);//缺少")"
        }
        void parameter_table()//＜参数表＞
        {
            while(symbol != SY.RPARSY)
            { 
                if (symbol != SY.INTSY)
                {
                    if (symbol == SY.IDSY)
                    {
                        error(21);
                        getsym();
                        if (symbol == SY.COMMASY)
                            getsym();
                    }
                }
                else if (symbol == SY.INTSY)
                {
                    int t = 0;
                    do
                    {
                        if (symbol == SY.INTSY)
                            getsym();
                        else
                            error(233);//缺少int
                        if (symbol == SY.IDSY)
                        {
                            add_symbol(TOKEN, 1, SY.INTNUMSY);
                            t++;
                            getsym();
                        }
                        else
                            error(9);//int后应为标识符
                        if (symbol == SY.COMMASY)
                            getsym();
                        else
                            break;
                    } while (true);
                    symbol_Item si;
                    search_symbol(currentFuntion_name, false, out si);
                    si.value = t;
                }
            }
        }
        void main_function()//＜主函数＞
        {
            SY s = 0;
            if (symbol == SY.VOIDSY)
            {
                hasreturn = true;
                s = SY.VOIDSY;
                getsym();
            }
            else if (symbol == SY.INTSY)
            {
                hasreturn = false;
                s = SY.INTSY;
                getsym();
            }
            else
                error(14);//main缺少返回类型
            if (symbol == SY.MAINSY)
            {
                add_symbol("main", 3, s);
                getsym();
            }
            else
                error(233);//缺少main
            parameter();
            state_compound();
            if (!hasreturn)
                error(22);//没有返回
        }
        void expression()//＜表达式＞
        {
            SY sy = symbol;
            if (sy == SY.MINUSSY || sy == SY.PLUSSY)
            {
                getsym();  
            }
            item();
            if (sy == SY.MINUSSY)
            {
                gene_code(Pcode.MUS);
            }          
            while (symbol == SY.PLUSSY || symbol == SY.MINUSSY || symbol == SY.INTNUMSY && (TOKEN[0] == '+' || TOKEN[0] == '-'))
            {
                if(symbol == SY.INTNUMSY)
                {
                    if (TOKEN[0] == '+')
                        symbol = SY.PLUSSY;
                    else
                        symbol = SY.MINUSSY;
                    current_pos -= TOKEN.Count() - 1;
                    TOKEN = TOKEN.Substring(0,1);
                }
                sy = symbol;
                getsym();
                item();
                if (sy == SY.MINUSSY)
                {
                    gene_code(Pcode.SUB);
                }
                else
                    gene_code(Pcode.ADD);
            }
        }
        void item()//＜项＞
        {
            SY sy;
            factor();
            while (symbol == SY.STARSY || symbol == SY.DIVISY)
            {
                sy = symbol;
                getsym();
                factor();
                if (sy == SY.STARSY)
                    gene_code(Pcode.MUL);
                else
                    gene_code(Pcode.DIV);
            }
        }
        void factor()//＜因子＞
        {
            if (symbol == SY.IDSY)
            {
                symbol_Item si;
                if (!search_symbol(TOKEN, false,out si))
                    error(10);//未声明的标识符
                if (si.kind == 3)
                {
                    call_function();
                }
                else if (si.kind == 1)
                {
                    gene_code(Pcode.LOD, si.lev, si.addr);
                    getsym();
                }
                else
                {
                    gene_code(Pcode.LDC, 0, si.value);
                    getsym();
                }
            }
            else if (symbol == SY.LPARSY)
            {
                getsym();
                expression();
                if (symbol == SY.RPARSY)
                    getsym();
                else
                    error(5);//缺少")"
            }
            else if (symbol == SY.INTNUMSY)
            {
                gene_code(Pcode.LDC, 0, int_num);
                getsym();
            }
            else
                error(17);//因子不合法
            
        }
        void statement()//＜语句＞
        {
            if (symbol == SY.IFSY)
                state_if();
            else if (symbol == SY.WHILESY)
                state_while();
            else if(symbol == SY.LBRACESY)
            {
                getsym();
                state_sequence();
                if (symbol == SY.RBRACESY)
                    getsym();
                else
                    error(6);//缺少"}"
            }
            else if(symbol == SY.IDSY)
            {
                save_condition();
                getsym();
                if (symbol == SY.LPARSY)
                {
                    recover_condition();
                    call_function();
                    if (symbol == SY.SEMISY)
                        getsym();
                    else
                        error(4);//缺少;
                }
                else if (symbol == SY.ASSIGNSY)
                {
                    recover_condition();
                    state_assign();
                    if (symbol == SY.SEMISY)
                        getsym();
                    else
                        error(4);//缺少;
                }
                else
                    recover_condition();

            }
            else if(symbol == SY.RETURNSY)
            {
                state_return();
                if (symbol == SY.SEMISY)
                    getsym();
                else
                    error(4);//缺少;
            }
            else if (symbol == SY.SCANFSY)
            {
                state_read();
                if (symbol == SY.SEMISY)
                    getsym();
                else
                    error(4);//缺少;
            }
            else if (symbol == SY.PRINTFSY)
            {
                state_write();
                if (symbol == SY.SEMISY)
                    getsym();
                else
                    error(4);//缺少;
            }
        }
        void state_assign()//＜赋值语句＞
        {
            if (symbol == SY.IDSY)
            {
                symbol_Item si;
                if (!search_symbol(TOKEN, false, out si))
                    error(10);//标识符未声明
                else
                    gene_code(Pcode.LDA,si.lev,si.addr);

                getsym();
                if (symbol == SY.ASSIGNSY)
                    getsym();
                else
                    error(13);//应为"="
                expression();
                gene_code(Pcode.STO);
            }
            else
                error(9);//应该是标识符
        }
        void state_if()//＜条件语句＞
        {
            if (symbol == SY.IFSY)
                getsym();
            if (symbol == SY.LPARSY)
                getsym();
            else
                error(7);//应为"("
            condition();
            if (symbol == SY.RPARSY)
                getsym();
            else
                error(5);//缺少")"
            gene_code(Pcode.JPC);
            int i = codes.Count-1;
            statement();   
            if(symbol == SY.ELSESY)
            {
                gene_code(Pcode.JMP);
                codes[i].val = codes.Count;
                i = codes.Count - 1;
                getsym();
                statement();
                codes[i].val = codes.Count;
            }
            else
                codes[i].val = codes.Count;
        }
        void condition()//＜条件＞
        {
            expression();
            if(symbol == SY.LESSSY || symbol == SY.LESSEQUSY || symbol == SY.GREATERSY || symbol == SY.GREATEREQUSY || symbol == SY.EQUSY || symbol == SY.NOTEQUSY)
            {
                SY sy = symbol;
                getsym();
                expression();
                if (sy == SY.LESSSY)
                    gene_code(Pcode.LSS);
                else if (sy == SY.LESSEQUSY)
                    gene_code(Pcode.LER);
                else if (sy == SY.GREATERSY)
                    gene_code(Pcode.GRT);
                else if (sy == SY.GREATEREQUSY)
                    gene_code(Pcode.GEQ);
                else if (sy == SY.EQUSY)
                    gene_code(Pcode.EQL);
                else
                    gene_code(Pcode.NEQ);
            }
        }
        void state_while()//＜循环语句＞
        {
            if (symbol == SY.WHILESY)
                getsym();
            if (symbol == SY.LPARSY)
            {
                getsym();
            }
            else
                error(7);//应为"("
            int u = codes.Count;
            condition();
            int i = codes.Count;
            gene_code(Pcode.JPC);
            if (symbol == SY.RPARSY)
            {
                getsym();
            }
            else
                error(5);//缺少")"
            statement();
            gene_code(Pcode.JMP,0,u);
            codes[i].val = codes.Count;
        }
        void call_function()//＜函数调用语句＞
        {
            if (symbol == SY.IDSY)
            {
                symbol_Item si;
                if (search_symbol(TOKEN, false, out si))
                {
                    if (si.kind != 3)
                       error(15);//该标识符不是函数
                    getsym();
                    gene_code(Pcode.MKS,0, symbol_table.IndexOf(si));
                }
                else
                    error(10);//标识符未声明
                if (symbol == SY.LPARSY)
                {
                    getsym();
                }
                else
                    error(7);//应为"("

                if (symbol == SY.RPARSY)
                {
                    getsym();
                }
                else
                {
                    parameter_value_table(si.value);
                    gene_code(Pcode.CAL,0,si.addr);
                    if (symbol == SY.RPARSY)
                    {
                        getsym();
                    }
                    else
                        error(5);//缺少")"
                }
            }
            else
                error(9);//应为标识符
        }
        void parameter_value_table(int num)//＜值参数表＞
        {
            int i = 0;
            expression();
            i++;
            while (symbol == SY.COMMASY)
            {
                getsym();
                expression();
                i++;
            }
            if (num > i)
                error(18);//参数量不足
            else if (num < i)
                error(19);//参数量过多
        }
        void state_sequence()//＜语句序列＞
        {
            int n;
            do
            {
                n = line_num;
                statement();
            } while (n != line_num);
        }
        void state_read()//＜读语句＞
        {
            if (symbol == SY.SCANFSY)
                getsym();
            if (symbol == SY.LPARSY)
            {
                getsym();
            }
            else
                error(7);//应为"("
            if (symbol == SY.IDSY)
            {
                symbol_Item si;
                if (search_symbol(TOKEN, false, out si))
                {
                    if (si.kind == 1)
                    {
                        gene_code(Pcode.LDA, si.lev, si.addr);
                        gene_code(Pcode.RED, 0, 1);
                    }
                    else
                        error(16);//应为变量

                }
                else
                    error(10);//标识符未声明
                getsym();
            }
            else
                error(9);//应为标识符
            if (symbol == SY.RPARSY)
            {
                getsym();
            }
            else
                error(5);//缺少")"
        }
        void state_write()//＜写语句＞
        {
            if (symbol == SY.PRINTFSY)
                getsym();
            if (symbol == SY.LPARSY)
            {
                getsym();
            }
            else
                error(7);//应为"("
            string s;
            if (symbol == SY.STRSY)
            {
                for (int i = 1; i < TOKEN.Length - 1; i++)
                    gene_code(Pcode.WRS, 0, TOKEN[i]);
                s = TOKEN;
                getsym();
                if (symbol == SY.COMMASY)
                    getsym();
                else
                    error(12);//缺少","

            }
            if (symbol != SY.RPARSY)
            {
                expression();
                gene_code(Pcode.WRW,0,1);
            }
            if (symbol == SY.RPARSY)
            {
                getsym();
            }
            else
                error(5);//缺少")"
        }
        void state_return()//＜返回语句＞
        {
            if (symbol == SY.RETURNSY)
            {
                getsym();
                symbol_Item si;
                search_symbol(currentFuntion_name, false, out si);
                if (si.type == SY.INTNUMSY)
                {
                    if (symbol == SY.LPARSY)
                    {
                        getsym();
                        expression();
                        if (symbol == SY.RPARSY)
                        {
                            getsym();
                        }
                        else
                            error(5);//缺少")"
                    }
                    else
                        error(7);//应为'('
                }
                gene_code(Pcode.EXF);
                hasreturn = true;
            }
        }

    

        //获取下一个字符
        private void GetChar()
        {
            Char = source[current_pos++];
        }
        //清除TOKEN
        void clearToken()
        {
            TOKEN = "";
        }
        //是否为空格
        bool isSpace()
        {
            if (Char == ' ') return true;
            else return false;
        }
        //是否为覆盖
        bool isNewline()
        {
            if (Char == '\n')
            {
                text_in.Dispatcher.Invoke(new MethodInvoker(delegate {
                    text_in.Text += string.Format("{0,-4}", line_num) + source.Substring(line_pos, current_pos - line_pos);
                }));
                line_pos = current_pos;
                line_num++;
                return true;
            }
            else if (Char == '\r') return true;
            else return false;
        }
        //是否为换表符
        bool isTab()
        {
            if (Char == '\t') return true;
            else return false;
        }
        //是否为字母
        bool isLetter()
        {
            if (Char >= 'A' && Char <= 'Z' || Char >= 'a' && Char <= 'z' || Char == '_') return true;
            else return false;
        }
        //是否为数字
        bool isDigit()
        {
            if (Char >= '0' && Char <= '9') return true;
            else return false;
        }
        //是否为0
        bool isZero()
        {
            if (Char == '0') return true;
            else return false;
        }
        //是否为非0数字
        bool isNotZeroDigit()
        {
            if (Char >= '1' && Char <= '9') return true;
            else return false;
        }
        //是否为点
        bool isDot()
        {
            if (Char == '.') return true;
            else return false;
        }
        //是否为小于
        bool isLess()
        {
            if (Char == '<') return true;
            else return false;
        }
        //是否为大于
        bool isGreater()
        {
            if (Char == '>') return true;
            else return false;
        }
        //是否为{
        bool isLbrace()
        {
            if (Char == '{') return true;
            else return false;
        }
        //是否为}
        bool isRbrace()
        {
            if (Char == '}') return true;
            else return false;
        }
        //是否为逗号
        bool isComma()
        {
            if (Char == ',') return true;
            else return false;
        }
        //是否为;
        bool isSemi()
        {
            if (Char == ';') return true;
            else return false;
        }
        //是否为!
        bool isExcl()
        {
            if (Char == '!') return true;
            else return false;
        }
        //是否为=
        bool isAss()
        {
            if (Char == '=') return true;
            else return false;
        }
        //是否为+
        bool isPlus()
        {
            if (Char == '+') return true;
            else return false;
        }
        //是否为-
        bool isMinus()
        {
            if (Char == '-') return true;
            else return false;
        }
        //是否为/
        bool isDivi()
        {
            if (Char == '/') return true;
            else return false;
        }
        //是否为*
        bool isStar()
        {
            if (Char == '*') return true;
            else return false;
        }
        //是否为(
        bool isLpar()
        {
            if (Char == '(') return true;
            else return false;
        }
        //是否为)
        bool isRpar()
        {
            if (Char == ')') return true;
            else return false;
        }
        //是否为引号
        bool isQuot()
        {
            if (Char == '\"') return true;
            else return false;
        }
        //截取当前字符到TOKEN
        void catToken()
        {
            TOKEN += Char;
        }
        //关闭键点击
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            System.Environment.Exit(0);
        }
        //停止运行点击
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            FreeConsole();
        }
        //字符回退
        void retract()
        {
            current_pos--;
            Char = source[current_pos - 1];
        }
        //保留字检测
        int reserver()
        {
            if (TOKEN == "const")
                return (int)SY.CONSTSY;
            else if (TOKEN == "int")
                return (int)SY.INTSY;
            else if (TOKEN == "void")
                return (int)SY.VOIDSY;
            else if (TOKEN == "if")
                return (int)SY.IFSY;
            else if (TOKEN == "else")
                return (int)SY.ELSESY;
            else if (TOKEN == "while")
                return (int)SY.WHILESY;
            else if (TOKEN == "main")
                return (int)SY.MAINSY;
            else if (TOKEN == "return")
                return (int)SY.RETURNSY;
            else if (TOKEN == "printf")
                return (int)SY.PRINTFSY;
            else if (TOKEN == "scanf")
                return (int)SY.SCANFSY;
            else
                return 0;
        }
        //不合法单词处理
        void wrongWord()
        {
            while (isLetter() || isDigit())
            {
                catToken();
                GetChar();
            }
            retract();
            error(2);
        }
        //词法分析遇到首部为0的处理
        void zero()
        {
            bool dot = true;
            bool wrong = false;
            catToken();
            GetChar();
            if (isDot())
            {
                dot = false;
            }
            while (!dot)
            {
                catToken();
                GetChar();
                if (!isDigit())
                {
                    break;
                }
            }
            if (isLetter())
            {
                wrong = true;
                wrongWord();
            }
            if (!wrong)
            {
                retract();
                if (dot)
                {
                    int_num = Convert.ToInt32(TOKEN);
                    symbol = SY.INTNUMSY;
                }
                else
                {
                    float_num = Convert.ToDouble(TOKEN);
                    symbol = SY.FLOATNUMSY;
                }
            }
        }
        //数值转换
        void num_trans()
        {
            bool dot = true, wrong = false;
            while (true)
            {
                if (dot && isDot())
                {
                    dot = false;
                }
                else if (!isDigit())
                {
                    if (isLetter())
                    {
                        wrong = true;
                        wrongWord();
                    }
                    break;
                }
                catToken();
                GetChar();
            }
            if (!wrong)
            {
                retract();
                if (dot)
                {
                    int_num = Convert.ToInt32(TOKEN);
                    symbol = SY.INTNUMSY;
                }
                else
                {
                    float_num = Convert.ToDouble(TOKEN);
                    symbol = SY.FLOATNUMSY;
                }
            }
        }
        //检查字符合法性
        bool charCheck()
        {
            if (Char < 7 || Char > 13) return true;
            else return false;
        }
        //获取下一个单词
        int getsym()
        {
            if(current_pos >= sizeOfFile)
            {
                return 2;
            }
            symbol = 0;
            clearToken();
            GetChar();
            while (isSpace() || isNewline() || isTab())
            {
                if (current_pos < sizeOfFile - 1)
                    GetChar();
                else
                    return 0;
            }
            if (isLetter())
            {
                while (isLetter() || isDigit())
                {
                    catToken();
                    GetChar();
                }
                retract();
                int resultValue = reserver();
                if (resultValue == 0)
                    symbol = SY.IDSY;
                else
                {
                    symbol = (SY)resultValue;
                }
            }
            else if (isNotZeroDigit())
            {
                num_trans();
            }
            else if (isZero())
            {
                zero();
            }
            else if (isPlus())
            {
                catToken();
                GetChar();
                if (isNotZeroDigit())
                {
                    num_trans();
                }
                else if (isZero())
                {
                    zero();
                }
                else
                {
                    retract();
                    symbol = SY.PLUSSY;
                }
            }
            else if (isMinus())
            {
                catToken();
                GetChar();
                if (isNotZeroDigit())
                {
                    num_trans();
                }
                else if (isZero())
                {
                    zero();
                }
                else
                {
                    retract();
                    symbol = SY.MINUSSY;
                }
            }
            else if (isStar())
            {
                catToken();
                symbol = SY.STARSY;
            }
            else if (isLess())
            {
                catToken();
                GetChar();
                if (isAss())
                {
                    catToken();
                    symbol = SY.LESSEQUSY;
                }
                else
                {
                    retract();
                    symbol = SY.LESSSY;
                }
            }
            else if (isGreater())
            {
                catToken();
                GetChar();
                if (isAss())
                {
                    catToken();
                    symbol = SY.GREATEREQUSY;
                }
                else
                {
                    retract();
                    symbol = SY.GREATERSY;
                }
            }
            else if (isExcl())
            {
                catToken();
                GetChar();
                if (isAss())
                {
                    catToken();
                    symbol = SY.NOTEQUSY;
                }
                else
                {
                    retract();
                    error(1);
                }
            }
            else if (isAss())
            {
                if (isAss())
                {
                    catToken();
                    symbol = SY.ASSIGNSY;
                }
                else
                {
                    retract();
                    symbol = SY.EQUSY;
                }
            }
            else if (isLpar())
            {
                catToken();
                symbol = SY.LPARSY;
            }
            else if (isRpar())
            {
                catToken();
                symbol = SY.RPARSY;
            }
            else if (isLbrace())
            {
                catToken();
                symbol = SY.LBRACESY;
            }
            else if (isRbrace())
            {
                catToken();
                symbol = SY.RBRACESY;
            }
            else if (isComma())
            {
                catToken();
                symbol = SY.COMMASY;
            }
            else if (isSemi())
            {
                catToken();
                symbol = SY.SEMISY;
            }
            else if (isQuot())
            {
                catToken();
                GetChar();
                bool outOfSize = false;
                while (!isQuot())
                {
                    if (charCheck())
                    {
                        catToken();
                    }
                    if (current_pos >= sizeOfFile - 1)
                    {
                        error(3);
                        outOfSize = true;
                        break;
                    }
                    GetChar();
                }
                if (!outOfSize)
                {
                    catToken();
                    symbol = SY.STRSY;
                }
            }
            else if (isDivi())
            {
                GetChar();
                if (isStar())
                {
                    do
                    {
                        do { GetChar(); } while (!isStar());
                        do
                        {
                            GetChar();
                            if (isDivi()) return 0;
                        } while (isStar());
                    } while (!isStar());
                }
                else if (isDivi())
                {
                    do { GetChar(); } while (!isNewline());
                    return 0;
                }
                else
                {
                    retract();
                    catToken();
                    symbol = SY.DIVISY;
                }
            }
            else error(1);
            return 1;
        }

        //错误处理
        void error(int id, bool isRuntime = false)
        {
            hasError = true;
            string s;
            if (isRuntime)
            {
                switch (id)
                {
                    case 1:
                        Console.WriteLine("输入错误");
                        break;
                    case 2:
                        Console.WriteLine("发生除0异常");
                        break;
                }
            }
            switch (id)
            {
                case 1:
                    catToken();
                    s = "字符不合法";
                    break;
                case 2:
                    catToken();
                    s ="单词不合法";
                    break;
                case 3:
                    catToken();
                    s ="缺少'\"'";
                    break;
                case 4:
                    s = "应为';'";
                    break;
                case 5:
                    s = "缺少')'";
                    break;
                case 6:
                    s = "缺少'}'";
                    break;
                case 7:
                    s = "应为'('";
                    break;
                case 8:
                    s = "应为'{'";
                    break;
                case 9:
                    s = "应为标识符";
                    break;
                case 10:
                    s = "标识符未声明";
                    break;
                case 11:
                    s = "标识符重复声明";
                    break;
                case 12:
                    s = "缺少','";
                    break;
                case 13:
                    s = "应为'='";
                    break;
                case 14:
                    s = "缺少返回值类型声明";
                    break;
                case 15:
                    s = "该标识符不是函数";
                    break;
                case 16:
                    s = "该标识符不是变量";
                    break;
                case 17:
                    s = "因子不合法";
                    break;
                case 18:
                    s = "函数参数数量不足";
                    break;
                case 19:
                    s = "函数参数数量过多";
                    break;
                case 20:
                    s = "语法不合法";
                    break;
                case 21:
                    s = "缺少类型";
                    break;
                case 22:
                    s = "函数缺少返回值";
                    break;
                default:
                    s = "未知错误";
                    break;
            }
            text_error.Dispatcher.Invoke(new MethodInvoker(delegate {
                    text_error.Text += "ERROR: 行 " + string.Format("{0,-8}", line_num) + string.Format("{0,-20}",TOKEN) + ":" + s + "\n";     
            }));
            if (!isRuntime && id == 4 )//缺少;的错误处理
            {
                    while (symbol != SY.SEMISY)
                        getsym();
                    getsym();
            }
        }
    }

}
