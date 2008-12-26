#region Copyright (C) 2005-2008 Team MediaPortal

/* 
 *	Copyright (C) 2005-2008 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */

#endregion

using System;
using System.Security.AccessControl;

namespace TvLibrary.Helper
{
  ///<summary>
  /// File Access Helper class
  ///</summary>
  public class FileAccessHelper
  {
    /// <summary>
    /// Grants full controll on the given file for everyone
    /// </summary>
    /// <param name="fileName">Filename</param>
    public static void GrantFullControll(string fileName)
    {
      if (!System.IO.File.Exists(fileName)) return;
      try
      {
        FileSecurity security = System.IO.File.GetAccessControl(fileName);
        FileSystemAccessRule newRule = new FileSystemAccessRule("EveryOne", FileSystemRights.FullControl, AccessControlType.Allow);
        security.AddAccessRule(newRule);
        System.IO.File.SetAccessControl(fileName, security);
      }
      catch (Exception ex)
      {
        Log.Log.WriteFile("Error while setting full write access to everyone for file: {0} : {1}",fileName,ex);
      }
    }
  }
}
