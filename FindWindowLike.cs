namespace WindowSearch
{
    using System.Collections;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Window Finding Utilities
    /// </summary>
    internal class FindWindowLike
    {
        private const int GWLID = -12;
        private const int GWHWNDNEXT = 2;
        private const int GWCHILD = 5;

        /// <summary>
        /// Find all windows matching a given title and class
        /// </summary>
        /// <param name="hwndStart">Beginning hwnd to start searching at</param>
        /// <param name="findText">Window title to search</param>
        /// <param name="findClassName">Class name to search</param>
        /// <returns>Returns an array of windows that matches the given arguments</returns>
        public static Window[] Find(int hwndStart, string findText, string findClassName)
        {
            ArrayList windows = DoSearch(hwndStart, findText, findClassName);

            return (Window[])windows.ToArray(typeof(Window));
        }

        private static ArrayList DoSearch(int hwndStart, string findText, string findClassName)
        {
            ArrayList list = new ArrayList();

            if (hwndStart == 0)
            {
                hwndStart = GetDesktopWindow();
            }

            int hwnd = GetWindow(hwndStart, GWCHILD);

            while (hwnd != 0)
            {
                // Recursively search for child windows.
                list.AddRange(DoSearch(hwnd, findText, findClassName));

                StringBuilder text = new StringBuilder(255);
                int rtn = GetWindowText(hwnd, text, 255);
                string windowText = text.ToString();
                windowText = windowText.Substring(0, rtn);

                StringBuilder cls = new StringBuilder(255);
                rtn = GetClassName(hwnd, cls, 255);
                string className = cls.ToString();
                className = className.Substring(0, rtn);

                if (GetParent(hwnd) != 0)
                {
                    rtn = GetWindowLong(hwnd, GWLID);
                }

                if (windowText.Length > 0 && windowText.StartsWith(findText) &&
                  (className.Length == 0 || className.StartsWith(findClassName)))
                {
                    Window currentWindow = new Window
                    {
                        Title = windowText,
                        Class = className,
                        Handle = hwnd
                    };

                    list.Add(currentWindow);
                }

                hwnd = GetWindow(hwnd, GWHWNDNEXT);
            }

            return list;
        }

        [DllImport("user32")]
        private static extern int GetWindow(int hwnd, int wCmd);

        [DllImport("user32")]
        private static extern int GetDesktopWindow();

        [DllImport("user32", EntryPoint = "GetWindowLongA")]
        private static extern int GetWindowLong(int hwnd, int nIndex);

        [DllImport("user32")]
        private static extern int GetParent(int hwnd);

        [DllImport("user32", EntryPoint = "GetClassNameA")]
        private static extern int GetClassName(
          int hWnd, [Out] StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32", EntryPoint = "GetWindowTextA")]
        private static extern int GetWindowText(
          int hWnd, [Out] StringBuilder lpString, int nMaxCount);

        /// <summary>
        /// Window Parameters
        /// </summary>
        internal class Window
        {
            /// <summary>
            /// Gets or sets window title
            /// </summary>
            internal string Title { get; set; }

            /// <summary>
            /// Gets or sets class name
            /// </summary>
            internal string Class { get; set; }

            /// <summary>
            /// Gets or sets handle
            /// </summary>
            internal int Handle { get; set; }
        }
    }
}