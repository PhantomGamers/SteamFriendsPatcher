# Steam Friends Patcher
This utility is designed to allow you to apply themes to the new Steam friends UI.  

# Instructions
To use manually, simply click "Force Check," by default this will run when you start the program.  
You will be required to do this everytime Valve pushes a new version of the friends.css file used by Steam.  
To avoid this, use the "Start Scanner" option and leave the program running.  
With the scanner option, whenever a new version of the friends.css is pushed it will automatically be patched.  
            
# Recommended options:  
* Run On Startup  
* Start Minimized  
* Minimize To Tray  
* Check For Updates  
* Auto Scan On Startup  

With these options configured, the program should be as easy as setup and forget!

# Todo/Bugs
* Implement CEF index parsing to better find the correct cache file (Pull Request welcome to anyone who has experience with this)  

# Dependencies
* [.NET Framework 4.6.1](https://www.microsoft.com/en-us/download/details.aspx?id=49981)

# Credits
* Darth from the Steam community forums for the method.
* [@henrikx for Steam directory detection code.](https://github.com/henrikx/metroskininstaller)
* [Sam Allen of Dot Net Perls for GZIP compression, decompression, and detection code.](https://www.dotnetperls.com/decompress)
* [Bob Learned of Experts Exchange for FindWindowLike code.](https://www.experts-exchange.com/questions/21611201/I-need-FindWindowLike-for-C.html)
