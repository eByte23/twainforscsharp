﻿///////////////////////////////////////////////////////////////////////////////////////
//
//  TwainWorkingGroup.TWAIN
//
//  This is a wrapper class for basic TWAIN functionality.  It establishes
//  behavior that every application should adhere to.  It also hides OS
//  specific details, so that toolkits or applications can use one unified
//  interface to TWAIN.
//
///////////////////////////////////////////////////////////////////////////////////////
//  Author          Date            TWAIN       Comment
//  M.McLaughlin    13-Sep-2015     2.3.1.2     DsmMem bug fixes
//  M.McLaughlin    26-Aug-2015     2.3.1.1     Log fix and sync with TWAIN Direct
//  M.McLaughlin    13-Mar-2015     2.3.1.0     Numerous fixes
//  M.McLaughlin    13-Oct-2014     2.3.0.4     Added logging
//  M.McLaughlin    24-Jun-2014     2.3.0.3     Stability fixes
//  M.McLaughlin    21-May-2014     2.3.0.2     64-Bit Linux
//  M.McLaughlin    27-Feb-2014     2.3.0.1     AnyCPU support
//  M.McLaughlin    21-Oct-2013     2.3.0.0     Initial Release
///////////////////////////////////////////////////////////////////////////////////////
//  Copyright (C) 2013-2015 Kodak Alaris Inc.
//
//  Permission is hereby granted, free of charge, to any person obtaining a
//  copy of this software and associated documentation files (the "Software"),
//  to deal in the Software without restriction, including without limitation
//  the rights to use, copy, modify, merge, publish, distribute, sublicense,
//  and/or sell copies of the Software, and to permit persons to whom the
//  Software is furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
//  THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
//  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
//  DEALINGS IN THE SOFTWARE.
///////////////////////////////////////////////////////////////////////////////////////

using ImageProcessorCore;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TWAINWorkingGroup
{
    public partial class TWAIN
    {
        ///////////////////////////////////////////////////////////////////////////////
        // Public Functions, these are the essentials...
        ///////////////////////////////////////////////////////////////////////////////

        #region Public Functions...

        /// <summary>
        /// Our constructor...
        /// </summary>
        /// <param name="a_szManufacturer">Application manufacturer</param>
        /// <param name="a_szProductFamily">Application product family</param>
        /// <param name="a_szProductName">Name of the application</param>
        /// <param name="a_u16ProtocolMajor">TWAIN protocol major (doesn't have to match TWAINH.CS)</param>
        /// <param name="a_u16ProtocolMinor">TWAIN protocol minor (doesn't have to match TWAINH.CS)</param>
        /// <param name="a_u32SupportedGroups">Bitmask of DG_ flags</param>
        /// <param name="a_twcy">Country code for the application</param>
        /// <param name="a_szInfo">Info about the application</param>
        /// <param name="a_twlg">Language code for the application</param>
        /// <param name="a_u16MajorNum">Application's major version</param>
        /// <param name="a_u16MinorNum">Application's minor version</param>
        /// <param name="a_blUseLegacyDSM">Use the legacy DSM (like TWAIN_32.DLL)</param>
        /// <param name="a_blUseCallbacks">Use callbacks instead of Windows post message</param>
        /// <param name="a_deviceeventback">Function to receive device events</param>
        /// <param name="a_scancallback">Function to handle scanning</param>
        /// <param name="a_runinuithreaddelegate">Help us run in the GUI thread on Windows</param>
        /// <param name="a_intptrHwnd">window handle</param>
        public TWAIN(string a_szManufacturer,
            string a_szProductFamily,
            string a_szProductName,
            ushort a_u16ProtocolMajor,
            ushort a_u16ProtocolMinor,
            uint a_u32SupportedGroups,
            TWCY a_twcy,
            string a_szInfo,
            TWLG a_twlg,
            ushort a_u16MajorNum,
            ushort a_u16MinorNum,
            bool a_blUseLegacyDSM,
            bool a_blUseCallbacks,
            DeviceEventCallback a_deviceeventback,
            ScanCallback a_scancallback,
            RunInUiThreadDelegate a_runinuithreaddelegate,
            IntPtr a_intptrHwnd
        )
        {
            TW_IDENTITY twidentity;

            // Since we're using P/Invoke in this sample, the DLL
            // is implicitly loaded as we access it, so we can
            // never go lower than state 2...
            m_state = STATE.S2;

            // Register the caller's info...
            twidentity = default(TW_IDENTITY);
            twidentity.Manufacturer.Set(a_szManufacturer);
            twidentity.ProductFamily.Set(a_szProductFamily);
            twidentity.ProductName.Set(a_szProductName);
            twidentity.ProtocolMajor = a_u16ProtocolMajor;
            twidentity.ProtocolMinor = a_u16ProtocolMinor;
            twidentity.SupportedGroups = a_u32SupportedGroups;
            twidentity.Version.Country = a_twcy;
            twidentity.Version.Info.Set(a_szInfo);
            twidentity.Version.Language = a_twlg;
            twidentity.Version.MajorNum = a_u16MajorNum;
            twidentity.Version.MinorNum = a_u16MinorNum;
            m_twidentityApp = twidentity;
            m_twidentitylegacyApp = TwidentityToTwidentitylegacy(twidentity);
            m_twidentitymacosxApp = TwidentityToTwidentitymacosx(twidentity);
            m_deviceeventcallback = a_deviceeventback;
            m_scancallback = a_scancallback;
            m_runinuithreaddelegate = a_runinuithreaddelegate;
            m_intptrHwnd = a_intptrHwnd;

            // Placeholder for our DS identity...
            m_twidentityDs = default(TW_IDENTITY);
            m_twidentitylegacyDs = default(TW_IDENTITY_LEGACY);
            m_twidentitymacosxDs = default(TW_IDENTITY_MACOSX);

            // We'll normally do an automatic get of DAT.STATUS, but if we'd
            // like to turn it off, this is the variable to hit...
            m_blAutoDatStatus = true;

            // Our helper functions from the DSM...
            m_twentrypointdelegates = default(TW_ENTRYPOINT_DELEGATES);

            // Our events...
            m_autoreseteventCaller = new AutoResetEvent(false);
            m_autoreseteventThread = new AutoResetEvent(false);
            m_autoreseteventRollback = new AutoResetEvent(false);
            m_autoreseteventThreadStarted = new AutoResetEvent(false);
            m_lockTwain = new object();

            // Windows only...
            if (ms_platform == Platform.WINDOWS)
            {
                m_blUseLegacyDSM = a_blUseLegacyDSM;
                m_blUseCallbacks = a_blUseCallbacks;
                m_windowsdsmentrycontrolcallbackdelegate = WindowsDsmEntryCallbackProxy;
            }

            // Linux only...
            else if (ms_platform == Platform.LINUX)
            {
                m_blUseLegacyDSM = false;
                m_blUseCallbacks = true;
                m_linuxdsmentrycontrolcallbackdelegate = LinuxDsmEntryCallbackProxy;
            }

            // Mac OS X only...
            else if (ms_platform == Platform.MACOSX)
            {
                m_blUseLegacyDSM = false;
                m_blUseCallbacks = true;
                m_macosxdsmentrycontrolcallbackdelegate = MacosxDsmEntryCallbackProxy;
            }

            // Uh-oh, Log will throw an exception for us...
            else
            {
                TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
            }

            // Activate our thread...
            if (m_threadTwain == null)
            {
                m_twaincommand = new TwainCommand();
                m_threadTwain = new Thread(Main);
                m_threadTwain.Start();
                if (!m_autoreseteventThreadStarted.WaitOne(5000))
                {
                    try
                    {
                        // m_threadTwain.Abort();
                        m_threadTwain = null;
                    }
                    catch
                    {
                        // Log will throw an exception for us...
                        TWAINWorkingGroup.Log.Assert("Failed to start the TWAIN background thread...");
                    }
                }
            }
        }

        /// <summary>
        /// Our destructor...
        /// </summary>
        ~TWAIN()
        {
            // Make sure that our thread is gone...
            if (m_threadTwain != null)
            {
                m_threadTwain.Join();
                m_threadTwain = null;
            }

            // Clean up our communication thingy...
            m_twaincommand = null;

            // Cleanup...
            m_autoreseteventCaller = null;
            m_autoreseteventThread = null;
            m_autoreseteventRollback = null;
            m_autoreseteventThreadStarted = null;
        }

        /// <summary>
        /// Alloc memory used with the data source...
        /// </summary>
        /// <param name="a_u32Size">Number of bytes to allocate</param>
        /// <returns>Point to memory</returns>
        public IntPtr DsmMemAlloc(uint a_u32Size)
        {
            IntPtr intptr;

            // Use the DSM...
            if (m_twentrypointdelegates.DSM_MemAllocate != null)
            {
                intptr = m_twentrypointdelegates.DSM_MemAllocate(a_u32Size);
                if (intptr == IntPtr.Zero)
                {
                    TWAINWorkingGroup.Log.Error("DSM_MemAllocate failed...");
                }
                return (intptr);
            }

            // Do it ourselves, Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                intptr = GlobalAlloc(a_u32Size, (UIntPtr)0x0042 /* GHND */);
                if (intptr == IntPtr.Zero)
                {
                    TWAINWorkingGroup.Log.Error("GlobalAlloc failed...");
                }
                return (intptr);
            }

            // Do it ourselves, Linux...
            if (ms_platform == Platform.LINUX)
            {
                intptr = Marshal.AllocHGlobal((int)a_u32Size);
                if (intptr == IntPtr.Zero)
                {
                    TWAINWorkingGroup.Log.Error("AllocHGlobal failed...");
                }
                return (intptr);
            }

            // Do it ourselves, Mac OS X...
            if (ms_platform == Platform.MACOSX)
            {
                IntPtr intptrIndirect = Marshal.AllocHGlobal((int)a_u32Size);
                if (intptrIndirect == IntPtr.Zero)
                {
                    TWAINWorkingGroup.Log.Error("AllocHGlobal(indirect) failed...");
                    return (intptrIndirect);
                }
                IntPtr intptrDirect = Marshal.AllocHGlobal(Marshal.SizeOf(intptrIndirect));
                if (intptrDirect == IntPtr.Zero)
                {
                    TWAINWorkingGroup.Log.Error("AllocHGlobal(direct) failed...");
                    return (intptrDirect);
                }
                Marshal.StructureToPtr(intptrIndirect, intptrDirect, true);
                return (intptrDirect);
            }

            // Trouble, Log will throw an exception for us...
            TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
            return (IntPtr.Zero);
        }

        /// <summary>
        /// Free memory used with the data source...
        /// </summary>
        /// <param name="a_intptrHandle">Pointer to free</param>
        public void DsmMemFree(ref IntPtr a_intptrHandle)
        {
            // Validate...
            if (a_intptrHandle == IntPtr.Zero)
            {
                return;
            }

            // Use the DSM...
            if (m_twentrypointdelegates.DSM_MemAllocate != null)
            {
                m_twentrypointdelegates.DSM_MemFree(a_intptrHandle);
            }

            // Do it ourselves, Windows...
            else if (ms_platform == Platform.WINDOWS)
            {
                GlobalFree(a_intptrHandle);
            }

            // Do it ourselves, Linux...
            else if (ms_platform == Platform.LINUX)
            {
                Marshal.FreeHGlobal(a_intptrHandle);
            }

            // Do it ourselves, Mac OS X...
            else if (ms_platform == Platform.MACOSX)
            {
                // Free the indirect pointer...
                IntPtr intptr = (IntPtr)Marshal.PtrToStructure(a_intptrHandle, typeof(IntPtr));
                if (intptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(intptr);
                }

                // If we free the direct pointer the CLR tells us that we're
                // freeing something that was never allocated.  We're going
                // to believe it and not do a free.  But I'm also leaving this
                // here as a record of the decision...
                //Marshal.FreeHGlobal(a_twcapability.hContainer);
            }

            // Make sure the variable is cleared...
            a_intptrHandle = IntPtr.Zero;
        }

        /// <summary>
        /// Lock memory used with the data source...
        /// </summary>
        /// <param name="a_intptrHandle">Handle to lock</param>
        /// <returns>Locked pointer</returns>
        public IntPtr DsmMemLock(IntPtr a_intptrHandle)
        {
            // Validate...
            if (a_intptrHandle == IntPtr.Zero)
            {
                return (a_intptrHandle);
            }

            // Use the DSM...
            if (m_twentrypointdelegates.DSM_MemLock != null)
            {
                return (m_twentrypointdelegates.DSM_MemLock(a_intptrHandle));
            }

            // Do it ourselves, Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                return (GlobalLock(a_intptrHandle));
            }

            // Do it ourselves, Linux...
            if (ms_platform == Platform.LINUX)
            {
                return (a_intptrHandle);
            }

            // Do it ourselves, Mac OS X...
            if (ms_platform == Platform.MACOSX)
            {
                IntPtr intptr = (IntPtr)Marshal.PtrToStructure(a_intptrHandle, typeof(IntPtr));
                return (intptr);
            }

            // Trouble, Log will throw an exception for us...
            TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
            return (IntPtr.Zero);
        }

        /// <summary>
        /// Unlock memory used with the data source...
        /// </summary>
        /// <param name="a_intptrHandle">Handle to unlock</param>
        public void DsmMemUnlock(IntPtr a_intptrHandle)
        {
            // Validate...
            if (a_intptrHandle == IntPtr.Zero)
            {
                return;
            }

            // Use the DSM...
            if (m_twentrypointdelegates.DSM_MemUnlock != null)
            {
                m_twentrypointdelegates.DSM_MemUnlock(a_intptrHandle);
                return;
            }

            // Do it ourselves, Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                GlobalUnlock(a_intptrHandle);
                return;
            }

            // Do it ourselves, Linux...
            if (ms_platform == Platform.LINUX)
            {
                return;
            }

            // Do it ourselves, Mac OS X...
            if (ms_platform == Platform.MACOSX)
            {
                return;
            }

            // Trouble, Log will throw an exception for us...
            TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
        }

        /// <summary>
        /// Report the current TWAIN state as we understand it...
        /// </summary>
        /// <returns>The current TWAIN state for the application</returns>
        public STATE GetState()
        {
            return (m_state);
        }

        /// <summary>
        /// True if the DSM has the DSM2 flag set...
        /// </summary>
        /// <returns>True if we detect the TWAIN Working Group open source DSM</returns>
        public bool IsDsm2()
        {
            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                return ((m_twidentitylegacyApp.SupportedGroups & (uint)DG.DSM2) != 0);
            }

            // Linux...
            if (ms_platform == Platform.LINUX)
            {
                return ((m_twidentitylegacyApp.SupportedGroups & (uint)DG.DSM2) != 0);
            }

            // Mac OS X...
            if (ms_platform == Platform.MACOSX)
            {
                return ((m_twidentitymacosxApp.SupportedGroups & (uint)DG.DSM2) != 0);
            }

            // Trouble, Log will throw an exception for us...
            TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
            return (false);
        }

        /// <summary>
        /// Have we seen the first image since MSG.ENABLEDS?
        /// </summary>
        /// <returns>True if the driver is ready to transfer images</returns>
        public bool IsMsgXferReady()
        {
            return (m_blIsMsgxferready);
        }

        /// <summary>
        /// Has the cancel button been pressed since the last MSG.ENABLEDS?
        /// </summary>
        /// <returns>True if the cancel button was pressed</returns>
        public bool IsMsgCloseDsReq()
        {
            return (m_blIsMsgclosedsreq);
        }

        /// <summary>
        /// Has the OK button been pressed since the last MSG.ENABLEDS?
        /// </summary>
        /// <returns>True if the OK button was pressed</returns>
        public bool IsMsgCloseDsOk()
        {
            return (m_blIsMsgclosedsok);
        }

        /// <summary>
        /// Monitor for DAT.NULL / MSG.* stuff...
        /// </summary>
        /// <param name="a_intptrHwnd">Window handle that we're monitoring</param>
        /// <param name="a_iMsg">A message</param>
        /// <param name="a_intptrWparam">a parameter for the message</param>
        /// <param name="a_intptrLparam">another parameter for the message</param>
        /// <returns></returns>
        public bool PreFilterMessage
        (
            IntPtr a_intptrHwnd,
            int a_iMsg,
            IntPtr a_intptrWparam,
            IntPtr a_intptrLparam
        )
        {
            STS sts;
            MESSAGE msg;

            // This is only in effect after MSG.ENABLEDS*...
            if (m_state < STATE.S5)
            {
                return (false);
            }

            // Convert the data...
            msg = new MESSAGE();
            msg.hwnd = a_intptrHwnd;
            msg.message = (uint)a_iMsg;
            msg.wParam = a_intptrWparam;
            msg.lParam = a_intptrLparam;

            // Allocate memory that we can give to the driver...
            if (m_tweventPreFilterMessage.pEvent == IntPtr.Zero)
            {
                m_tweventPreFilterMessage.pEvent = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
            }
            Marshal.StructureToPtr(msg, m_tweventPreFilterMessage.pEvent, true);

            // See if the driver wants the event...
            m_tweventPreFilterMessage.TWMessage = 0;
            sts = DatEvent(DG.CONTROL, MSG.PROCESSEVENT, ref m_tweventPreFilterMessage);
            if ((sts != STS.DSEVENT) && (sts != STS.NOTDSEVENT))
            {
                return (false);
            }

            // Handle messages...
            ProcessEvent((MSG)m_tweventPreFilterMessage.TWMessage);

            // All done, tell the app we consumed the event if we
            // got back a status telling us that...
            return (sts == STS.DSEVENT);
        }

        /// <summary>
        /// Rollback the TWAIN state machine to the specified value, with an
        /// automatic resync if it detects a sequence error...
        /// </summary>
        /// <param name="a_stateTarget">The TWAIN state that we want to end up at</param>
        private static int s_iCloseDsmDelay = 0;

        public void Rollback(STATE a_stateTarget)
        {
            int iRetry;
            STS sts;
            STATE stateStart;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set the command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.stateRollback = a_stateTarget;
                    threaddata.blRollback = true;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply, the delay
                    // is needed because Mac OS X doesn't gracefully handle
                    // the loss of a mutex...
                    s_iCloseDsmDelay = 0;
                    CallerToThreadSet();
                    ThreadToRollbackWaitOne();
                    System.Threading.Thread.Sleep(s_iCloseDsmDelay);

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return;
            }

            // If we get a sequence error, then we'll repeat the loop from
            // the highest possible state to see if we can fix the problem...
            iRetry = 2;
            stateStart = GetState();
            while (iRetry-- > 0)
            {
                // State 7 --> State 6...
                if ((stateStart >= STATE.S7) && (a_stateTarget < STATE.S7))
                {
                    TW_PENDINGXFERS twpendingxfers = default(TW_PENDINGXFERS);
                    sts = DatPendingxfers(DG.CONTROL, MSG.ENDXFER, ref twpendingxfers);
                    if (sts == STS.SEQERROR)
                    {
                        stateStart = STATE.S7;
                        continue;
                    }
                    stateStart = STATE.S6;
                }

                // State 6 --> State 5...
                if ((stateStart >= STATE.S6) && (a_stateTarget < STATE.S6))
                {
                    TW_PENDINGXFERS twpendingxfers = default(TW_PENDINGXFERS);
                    sts = DatPendingxfers(DG.CONTROL, MSG.RESET, ref twpendingxfers);
                    if (sts == STS.SEQERROR)
                    {
                        stateStart = STATE.S7;
                        continue;
                    }
                    stateStart = STATE.S5;
                }

                // State 5 --> State 4...
                if ((stateStart >= STATE.S5) && (a_stateTarget < STATE.S5))
                {
                    TW_USERINTERFACE twuserinterface = default(TW_USERINTERFACE);
                    sts = DatUserinterface(DG.CONTROL, MSG.DISABLEDS, ref twuserinterface);
                    if (sts == STS.SEQERROR)
                    {
                        stateStart = STATE.S7;
                        continue;
                    }
                    if (m_tweventPreFilterMessage.pEvent != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(m_tweventPreFilterMessage.pEvent);
                        m_tweventPreFilterMessage.pEvent = IntPtr.Zero;
                    }
                    stateStart = STATE.S4;
                    m_blAcceptXferReady = false;
                }

                // State 4 --> State 3...
                if ((stateStart >= STATE.S4) && (a_stateTarget < STATE.S4))
                {
                    sts = DatIdentity(DG.CONTROL, MSG.CLOSEDS, ref m_twidentityDs);
                    if (sts == STS.SEQERROR)
                    {
                        stateStart = STATE.S7;
                        continue;
                    }
                    stateStart = STATE.S3;
                }

                // State 3 --> State 2...
                if ((stateStart >= STATE.S3) && (a_stateTarget < STATE.S3))
                {
                    // Do this to prevent a deadlock on Mac OS X, two seconds
                    // better be enough to finish up...
                    if (GetPlatform() == Platform.MACOSX)
                    {
                        ThreadToRollbackSet();
                        s_iCloseDsmDelay = 2000;
                    }

                    // Now do the rest of it...
                    sts = DatParent(DG.CONTROL, MSG.CLOSEDSM, ref m_intptrHwnd);
                    if (sts == STS.SEQERROR)
                    {
                        stateStart = STATE.S7;
                        continue;
                    }
                    stateStart = STATE.S2;
                }

                // All done...
                break;
            }
        }

        #endregion Public Functions...

        ///////////////////////////////////////////////////////////////////////////////
        // Public Helper Functions, we're mapping TWAIN structures to strings to
        // make it easier for the application to work with the data.  All of the
        // functions that do that are located here...
        ///////////////////////////////////////////////////////////////////////////////

        #region Public Helper Functions...

        /// <summary>
        /// Convert the contents of a callback to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twcallback">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string CallbackToCsv(TW_CALLBACK a_twcallback)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twcallback.CallBackProc.ToString());
                csv.Add(a_twcallback.RefCon.ToString());
                csv.Add(a_twcallback.Message.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to an callback structure...
        /// </summary>
        /// <param name="a_twcallback">A TWAIN structure</param>
        /// <param name="a_szCallback">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToCallback(ref TW_CALLBACK a_twcallback, string a_szCallback)
        {
            // Init stuff...
            a_twcallback = default(TW_CALLBACK);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szCallback);

                // Grab the values...
                a_twcallback.CallBackProc = (IntPtr)UInt64.Parse(asz[0]);
                a_twcallback.RefCon = uint.Parse(asz[1]);
                a_twcallback.Message = ushort.Parse(asz[2]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a callback2 to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twcallback2">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string Callback2ToCsv(TW_CALLBACK2 a_twcallback2)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twcallback2.CallBackProc.ToString());
                csv.Add(a_twcallback2.RefCon.ToString());
                csv.Add(a_twcallback2.Message.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to an callback2 structure...
        /// </summary>
        /// <param name="a_twcallback2">A TWAIN structure</param>
        /// <param name="a_szCallback2">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToCallback2(ref TW_CALLBACK2 a_twcallback2, string a_szCallback2)
        {
            // Init stuff...
            a_twcallback2 = default(TW_CALLBACK2);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szCallback2);

                // Grab the values...
                a_twcallback2.CallBackProc = (IntPtr)UInt64.Parse(asz[0]);
                a_twcallback2.RefCon = (UIntPtr)UInt64.Parse(asz[1]);
                a_twcallback2.Message = ushort.Parse(asz[2]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a capability to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twcapability">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string CapabilityToCsv(TW_CAPABILITY a_twcapability)
        {
            IntPtr intptr;
            IntPtr intptrLocked;
            TWTY ItemType;
            uint NumItems;

            // Handle the container...
            switch (a_twcapability.ConType)
            {
                default:
                    return ("(unrecognized container)");

                case TWON.ARRAY:
                    {
                        uint uu;
                        CSV csvArray;

                        // Mac has a level of indirection and a different structure (ick)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Crack the container...
                            TW_ARRAY_MACOSX twarraymacosx = default(TW_ARRAY_MACOSX);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twarraymacosx = (TW_ARRAY_MACOSX)Marshal.PtrToStructure(intptrLocked, typeof(TW_ARRAY_MACOSX));
                            ItemType = (TWTY)twarraymacosx.ItemType;
                            NumItems = twarraymacosx.NumItems;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twarraymacosx));
                        }
                        else
                        {
                            // Crack the container...
                            TW_ARRAY twarray = default(TW_ARRAY);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twarray = (TW_ARRAY)Marshal.PtrToStructure(intptrLocked, typeof(TW_ARRAY));
                            ItemType = twarray.ItemType;
                            NumItems = twarray.NumItems;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twarray));
                        }

                        // Start building the string...
                        csvArray = Common(a_twcapability.Cap, a_twcapability.ConType, ItemType);
                        csvArray.Add(NumItems.ToString());

                        // Tack on the stuff from the ItemList...
                        for (uu = 0; uu < NumItems; uu++)
                        {
                            csvArray.Add(GetIndexedItem(ItemType, intptr, (int)uu));
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (csvArray.Get());
                    }

                case TWON.ENUMERATION:
                    {
                        uint uu;
                        CSV csvEnum;

                        // Mac has a level of indirection and a different structure (ick)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Crack the container...
                            TW_ENUMERATION_MACOSX twenumerationmacosx = default(TW_ENUMERATION_MACOSX);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twenumerationmacosx = (TW_ENUMERATION_MACOSX)Marshal.PtrToStructure(intptrLocked, typeof(TW_ENUMERATION_MACOSX));
                            ItemType = (TWTY)twenumerationmacosx.ItemType;
                            NumItems = twenumerationmacosx.NumItems;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twenumerationmacosx));

                            // Start building the string...
                            csvEnum = Common(a_twcapability.Cap, a_twcapability.ConType, ItemType);
                            csvEnum.Add(NumItems.ToString());
                            csvEnum.Add(twenumerationmacosx.CurrentIndex.ToString());
                            csvEnum.Add(twenumerationmacosx.DefaultIndex.ToString());
                        }
                        else
                        {
                            // Crack the container...
                            TW_ENUMERATION twenumeration = default(TW_ENUMERATION);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twenumeration = (TW_ENUMERATION)Marshal.PtrToStructure(intptrLocked, typeof(TW_ENUMERATION));
                            ItemType = twenumeration.ItemType;
                            NumItems = twenumeration.NumItems;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twenumeration));

                            // Start building the string...
                            csvEnum = Common(a_twcapability.Cap, a_twcapability.ConType, ItemType);
                            csvEnum.Add(NumItems.ToString());
                            csvEnum.Add(twenumeration.CurrentIndex.ToString());
                            csvEnum.Add(twenumeration.DefaultIndex.ToString());
                        }

                        // Tack on the stuff from the ItemList...
                        for (uu = 0; uu < NumItems; uu++)
                        {
                            csvEnum.Add(GetIndexedItem(ItemType, intptr, (int)uu));
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (csvEnum.Get());
                    }

                case TWON.ONEVALUE:
                    {
                        CSV csvOnevalue;

                        // Mac has a level of indirection and a different structure (ick)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Crack the container...
                            TW_ONEVALUE_MACOSX twonevaluemacosx = default(TW_ONEVALUE_MACOSX);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twonevaluemacosx = (TW_ONEVALUE_MACOSX)Marshal.PtrToStructure(intptrLocked, typeof(TW_ONEVALUE_MACOSX));
                            ItemType = (TWTY)twonevaluemacosx.ItemType;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twonevaluemacosx));
                        }
                        else
                        {
                            // Crack the container...
                            TW_ONEVALUE twonevalue = default(TW_ONEVALUE);
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twonevalue = (TW_ONEVALUE)Marshal.PtrToStructure(intptrLocked, typeof(TW_ONEVALUE));
                            ItemType = (TWTY)twonevalue.ItemType;
                            intptr = (IntPtr)((UInt64)intptrLocked + (UInt64)Marshal.SizeOf(twonevalue));
                        }

                        // Start building the string...
                        csvOnevalue = Common(a_twcapability.Cap, a_twcapability.ConType, ItemType);

                        // Tack on the stuff from the Item...
                        csvOnevalue.Add(GetIndexedItem(ItemType, intptr, 0));

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (csvOnevalue.Get());
                    }

                case TWON.RANGE:
                    {
                        CSV csvRange;
                        TW_RANGE twrange;
                        TW_RANGE_MACOSX twrangemacosx;
                        TW_RANGE_FIX32 twrangefix32;
                        TW_RANGE_FIX32_MACOSX twrangefix32macosx;

                        // Mac has a level of indirection and a different structure (ick)...
                        twrange = default(TW_RANGE);
                        if (ms_platform == Platform.MACOSX)
                        {
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twrangemacosx = (TW_RANGE_MACOSX)Marshal.PtrToStructure(intptrLocked, typeof(TW_RANGE_MACOSX));
                            twrangefix32macosx = (TW_RANGE_FIX32_MACOSX)Marshal.PtrToStructure(intptrLocked, typeof(TW_RANGE_FIX32_MACOSX));
                            twrange.ItemType = (TWTY)twrangemacosx.ItemType;
                            twrange.MinValue = twrangemacosx.MinValue;
                            twrange.MaxValue = twrangemacosx.MaxValue;
                            twrange.StepSize = twrangemacosx.StepSize;
                            twrange.DefaultValue = twrangemacosx.DefaultValue;
                            twrange.CurrentValue = twrangemacosx.CurrentValue;
                            twrangefix32.ItemType = (TWTY)twrangefix32macosx.ItemType;
                            twrangefix32.MinValue = twrangefix32macosx.MinValue;
                            twrangefix32.MaxValue = twrangefix32macosx.MaxValue;
                            twrangefix32.StepSize = twrangefix32macosx.StepSize;
                            twrangefix32.DefaultValue = twrangefix32macosx.DefaultValue;
                            twrangefix32.CurrentValue = twrangefix32macosx.CurrentValue;
                        }
                        else
                        {
                            intptrLocked = DsmMemLock(a_twcapability.hContainer);
                            twrange = (TW_RANGE)Marshal.PtrToStructure(intptrLocked, typeof(TW_RANGE));
                            twrangefix32 = (TW_RANGE_FIX32)Marshal.PtrToStructure(intptrLocked, typeof(TW_RANGE_FIX32));
                        }

                        // Start the string...
                        csvRange = Common(a_twcapability.Cap, a_twcapability.ConType, twrange.ItemType);

                        // Tack on the data...
                        switch ((TWTY)twrange.ItemType)
                        {
                            default:
                                DsmMemUnlock(a_twcapability.hContainer);
                                return ("(Get Capability: unrecognized data type)");

                            case TWTY.INT8:
                                csvRange.Add(((char)(twrange.MinValue)).ToString());
                                csvRange.Add(((char)(twrange.MaxValue)).ToString());
                                csvRange.Add(((char)(twrange.StepSize)).ToString());
                                csvRange.Add(((char)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((char)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.INT16:
                                csvRange.Add(((short)(twrange.MinValue)).ToString());
                                csvRange.Add(((short)(twrange.MaxValue)).ToString());
                                csvRange.Add(((short)(twrange.StepSize)).ToString());
                                csvRange.Add(((short)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((short)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.INT32:
                                csvRange.Add(((int)(twrange.MinValue)).ToString());
                                csvRange.Add(((int)(twrange.MaxValue)).ToString());
                                csvRange.Add(((int)(twrange.StepSize)).ToString());
                                csvRange.Add(((int)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((int)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.UINT8:
                                csvRange.Add(((byte)(twrange.MinValue)).ToString());
                                csvRange.Add(((byte)(twrange.MaxValue)).ToString());
                                csvRange.Add(((byte)(twrange.StepSize)).ToString());
                                csvRange.Add(((byte)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((byte)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.BOOL:
                            case TWTY.UINT16:
                                csvRange.Add(((ushort)(twrange.MinValue)).ToString());
                                csvRange.Add(((ushort)(twrange.MaxValue)).ToString());
                                csvRange.Add(((ushort)(twrange.StepSize)).ToString());
                                csvRange.Add(((ushort)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((ushort)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.UINT32:
                                csvRange.Add(((uint)(twrange.MinValue)).ToString());
                                csvRange.Add(((uint)(twrange.MaxValue)).ToString());
                                csvRange.Add(((uint)(twrange.StepSize)).ToString());
                                csvRange.Add(((uint)(twrange.DefaultValue)).ToString());
                                csvRange.Add(((uint)(twrange.CurrentValue)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());

                            case TWTY.FIX32:
                                csvRange.Add(((double)twrangefix32.MinValue.Whole + ((double)twrangefix32.MinValue.Frac / 65536.0)).ToString());
                                csvRange.Add(((double)twrangefix32.MaxValue.Whole + ((double)twrangefix32.MaxValue.Frac / 65536.0)).ToString());
                                csvRange.Add(((double)twrangefix32.StepSize.Whole + ((double)twrangefix32.StepSize.Frac / 65536.0)).ToString());
                                csvRange.Add(((double)twrangefix32.DefaultValue.Whole + ((double)twrangefix32.DefaultValue.Frac / 65536.0)).ToString());
                                csvRange.Add(((double)twrangefix32.CurrentValue.Whole + ((double)twrangefix32.CurrentValue.Frac / 65536.0)).ToString());
                                DsmMemUnlock(a_twcapability.hContainer);
                                return (csvRange.Get());
                        }
                    }
            }
        }

        /// <summary>
        /// Convert the contents of a string into something we can poke into a
        /// TW_CAPABILITY structure...
        /// </summary>
        /// <param name="a_twcapability">A TWAIN structure</param>
        /// <param name="a_szSetting">A CSV string of the TWAIN structure</param>
        /// <param name="a_szValue">The container for this capability</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToCapability(ref TW_CAPABILITY a_twcapability, ref string a_szSetting, string a_szValue)
        {
            int ii;
            TWTY twty;
            uint u32NumItems;
            IntPtr intptr;
            string szResult;
            string[] asz;

            // We need some protection for this one...
            try
            {
                // Tokenize our values...
                asz = CSV.Parse(a_szValue);
                if (asz.GetLength(0) < 4)
                {
                    a_szSetting = "Set Capability: (insufficient number of arguments)";
                    return (false);
                }

                // Set the capability from text or hex...
                try
                {
                    a_twcapability.Cap = (CAP)Enum.Parse(typeof(CAP), asz[0], true);
                }
                catch
                {
                    a_twcapability.Cap = (CAP)0xFFFF;
                }
                if ((a_twcapability.Cap == (CAP)0xFFFF) || !asz[0].Contains("_"))
                {
                    a_twcapability.Cap = (CAP)Convert.ToUInt16(asz[0], 16);
                }

                // Set the container from text or decimal...
                try
                {
                    a_twcapability.ConType = (TWON)Enum.Parse(typeof(TWON), asz[1].Replace("TWON_", ""), true);
                }
                catch
                {
                    a_twcapability.ConType = (TWON)ushort.Parse(asz[1]);
                }

                // Set the item type from text or decimal...
                try
                {
                    twty = (TWTY)Enum.Parse(typeof(TWTY), asz[2].Replace("TWTY_", ""), true);
                }
                catch
                {
                    twty = (TWTY)ushort.Parse(asz[2]);
                }

                // Assign the new value...
                switch (a_twcapability.ConType)
                {
                    default:
                        a_szSetting = "(unrecognized container)";
                        return (false);

                    case TWON.ARRAY:
                        // Validate...
                        if (asz.GetLength(0) < 4)
                        {
                            a_szSetting = "Set Capability: (insufficient number of arguments)";
                            return (false);
                        }

                        // Get the values...
                        u32NumItems = uint.Parse(asz[3]);

                        // Allocate the container (go for worst case, which is TW_STR255)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ARRAY_MACOSX)) + (((int)u32NumItems + 1) * Marshal.SizeOf(default(TW_STR255)))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ARRAY_MACOSX twarraymacosx = default(TW_ARRAY_MACOSX);
                            twarraymacosx.ItemType = (uint)twty;
                            twarraymacosx.NumItems = u32NumItems;
                            Marshal.StructureToPtr(twarraymacosx, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twarraymacosx));
                        }
                        else
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ARRAY)) + (((int)u32NumItems + 1) * Marshal.SizeOf(default(TW_STR255)))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ARRAY twarray = default(TW_ARRAY);
                            twarray.ItemType = twty;
                            twarray.NumItems = u32NumItems;
                            Marshal.StructureToPtr(twarray, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twarray));
                        }

                        // Set the ItemList...
                        for (ii = 0; ii < u32NumItems; ii++)
                        {
                            szResult = SetIndexedItem(a_twcapability.ConType, twty, intptr, ii, asz[ii + 4]);
                            if (szResult != "")
                            {
                                return (false);
                            }
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (true);

                    case TWON.ENUMERATION:
                        // Validate...
                        if (asz.GetLength(0) < 6)
                        {
                            a_szSetting = "Set Capability: (insufficient number of arguments)";
                            return (false);
                        }

                        // Get the values...
                        u32NumItems = uint.Parse(asz[3]);

                        // Allocate the container (go for worst case, which is TW_STR255)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ENUMERATION_MACOSX)) + (((int)u32NumItems + 1) * Marshal.SizeOf(default(TW_STR255)))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ENUMERATION_MACOSX twenumerationmacosx = default(TW_ENUMERATION_MACOSX);
                            twenumerationmacosx.ItemType = (uint)twty;
                            twenumerationmacosx.NumItems = u32NumItems;
                            twenumerationmacosx.CurrentIndex = uint.Parse(asz[4]);
                            twenumerationmacosx.DefaultIndex = uint.Parse(asz[5]);
                            Marshal.StructureToPtr(twenumerationmacosx, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twenumerationmacosx));
                        }
                        else
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ENUMERATION)) + (((int)u32NumItems + 1) * Marshal.SizeOf(default(TW_STR255)))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ENUMERATION twenumeration = default(TW_ENUMERATION);
                            twenumeration.ItemType = twty;
                            twenumeration.NumItems = u32NumItems;
                            twenumeration.CurrentIndex = uint.Parse(asz[4]);
                            twenumeration.DefaultIndex = uint.Parse(asz[5]);
                            Marshal.StructureToPtr(twenumeration, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twenumeration));
                        }

                        // Set the ItemList...
                        for (ii = 0; ii < u32NumItems; ii++)
                        {
                            szResult = SetIndexedItem(a_twcapability.ConType, twty, intptr, ii, asz[ii + 6]);
                            if (szResult != "")
                            {
                                return (false);
                            }
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (true);

                    case TWON.ONEVALUE:
                        // Validate...
                        if (asz.GetLength(0) < 4)
                        {
                            a_szSetting = "Set Capability: (insufficient number of arguments)";
                            return (false);
                        }

                        // Allocate the container (go for worst case, which is TW_STR255)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ONEVALUE_MACOSX)) + Marshal.SizeOf(default(TW_STR255))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ONEVALUE_MACOSX twonevaluemacosx = default(TW_ONEVALUE_MACOSX);
                            twonevaluemacosx.ItemType = (uint)twty;
                            Marshal.StructureToPtr(twonevaluemacosx, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twonevaluemacosx));
                        }
                        else
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_ONEVALUE)) + Marshal.SizeOf(default(TW_STR255))));
                            intptr = DsmMemLock(a_twcapability.hContainer);

                            // Set the meta data...
                            TW_ONEVALUE twonevalue = default(TW_ONEVALUE);
                            twonevalue.ItemType = twty;
                            Marshal.StructureToPtr(twonevalue, intptr, true);

                            // Get the pointer to the ItemList...
                            intptr = (IntPtr)((UInt64)intptr + (UInt64)Marshal.SizeOf(twonevalue));
                        }

                        // Set the Item...
                        szResult = SetIndexedItem(a_twcapability.ConType, twty, intptr, 0, asz[3]);
                        if (szResult != "")
                        {
                            return (false);
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (true);

                    case TWON.RANGE:
                        // Validate...
                        if (asz.GetLength(0) < 8)
                        {
                            a_szSetting = "Set Capability: (insufficient number of arguments)";
                            return (false);
                        }

                        // Allocate the container (go for worst case, which is TW_STR255)...
                        if (ms_platform == Platform.MACOSX)
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_RANGE_MACOSX))));
                            intptr = DsmMemLock(a_twcapability.hContainer);
                        }
                        else
                        {
                            // Allocate...
                            a_twcapability.hContainer = DsmMemAlloc((uint)(Marshal.SizeOf(default(TW_RANGE))));
                            intptr = DsmMemLock(a_twcapability.hContainer);
                        }

                        // Set the Item...
                        szResult = SetRangeItem(twty, intptr, asz);
                        if (szResult != "")
                        {
                            return (false);
                        }

                        // All done...
                        DsmMemUnlock(a_twcapability.hContainer);
                        return (true);
                }
            }
            catch
            {
                a_szValue = "(data error)";
                return (false);
            }
        }

        /// <summary>
        /// Convert the contents of a custom DS data to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twcustomdsdata">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string CustomdsdataToCsv(TW_CUSTOMDSDATA a_twcustomdsdata)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twcustomdsdata.InfoLength.ToString());
                IntPtr intptr = DsmMemLock(a_twcustomdsdata.hData);
                csv.Add(intptr.ToString());
                DsmMemUnlock(a_twcustomdsdata.hData);
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to a custom DS data structure...
        /// </summary>
        /// <param name="a_twcustomdsdata">A TWAIN structure</param>
        /// <param name="a_szCustomdsdata">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToCustomdsdata(ref TW_CUSTOMDSDATA a_twcustomdsdata, string a_szCustomdsdata)
        {
            // Init stuff...
            a_twcustomdsdata = default(TW_CUSTOMDSDATA);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szCustomdsdata);

                // Grab the values...
                a_twcustomdsdata.InfoLength = uint.Parse(asz[0]);
                a_twcustomdsdata.hData = DsmMemAlloc(a_twcustomdsdata.InfoLength);
                IntPtr intptr = DsmMemLock(a_twcustomdsdata.hData);
                byte[] bProfile = new byte[a_twcustomdsdata.InfoLength];
                Marshal.Copy((IntPtr)UInt64.Parse(asz[1]), bProfile, 0, (int)a_twcustomdsdata.InfoLength);
                Marshal.Copy(bProfile, 0, intptr, (int)a_twcustomdsdata.InfoLength);
                DsmMemUnlock(a_twcustomdsdata.hData);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a device event to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twdeviceevent">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string DeviceeventToCsv(TW_DEVICEEVENT a_twdeviceevent)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(((TWDE)a_twdeviceevent.Event).ToString());
                csv.Add(a_twdeviceevent.DeviceName.Get());
                csv.Add(a_twdeviceevent.BatteryMinutes.ToString());
                csv.Add(a_twdeviceevent.BatteryPercentage.ToString());
                csv.Add(a_twdeviceevent.PowerSupply.ToString());
                csv.Add(((double)a_twdeviceevent.XResolution.Whole + ((double)a_twdeviceevent.XResolution.Frac / 65536.0)).ToString());
                csv.Add(((double)a_twdeviceevent.YResolution.Whole + ((double)a_twdeviceevent.YResolution.Frac / 65536.0)).ToString());
                csv.Add(a_twdeviceevent.FlashUsed2.ToString());
                csv.Add(a_twdeviceevent.AutomaticCapture.ToString());
                csv.Add(a_twdeviceevent.TimeBeforeFirstCapture.ToString());
                csv.Add(a_twdeviceevent.TimeBetweenCaptures.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of an entry point to a string that
        /// we can show in our simple GUI...
        /// </summary>
        /// <param name="a_twentrypoint">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string EntrypointToCsv(TW_ENTRYPOINT a_twentrypoint)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twentrypoint.Size.ToString());
                csv.Add("0x" + ((a_twentrypoint.DSM_Entry == null) ? "0" : a_twentrypoint.DSM_Entry.ToString("X")));
                csv.Add("0x" + ((a_twentrypoint.DSM_MemAllocate == null) ? "0" : a_twentrypoint.DSM_MemAllocate.ToString("X")));
                csv.Add("0x" + ((a_twentrypoint.DSM_MemFree == null) ? "0" : a_twentrypoint.DSM_MemFree.ToString("X")));
                csv.Add("0x" + ((a_twentrypoint.DSM_MemLock == null) ? "0" : a_twentrypoint.DSM_MemLock.ToString("X")));
                csv.Add("0x" + ((a_twentrypoint.DSM_MemUnlock == null) ? "0" : a_twentrypoint.DSM_MemUnlock.ToString("X")));
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a filesystem string to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twfilesystem">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string FilesystemToCsv(TW_FILESYSTEM a_twfilesystem)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twfilesystem.InputName.Get());
                csv.Add(a_twfilesystem.OutputName.Get());
                csv.Add(a_twfilesystem.Context.ToString());
                csv.Add(a_twfilesystem.Recursive.ToString());
                csv.Add(a_twfilesystem.FileType.ToString());
                csv.Add(a_twfilesystem.Size.ToString());
                csv.Add(a_twfilesystem.CreateTimeDate.Get());
                csv.Add(a_twfilesystem.ModifiedTimeDate.Get());
                csv.Add(a_twfilesystem.FreeSpace.ToString());
                csv.Add(a_twfilesystem.NewImageSize.ToString());
                csv.Add(a_twfilesystem.NumberOfFiles.ToString());
                csv.Add(a_twfilesystem.NumberOfSnippets.ToString());
                csv.Add(a_twfilesystem.DeviceGroupMask.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to a filesystem structure...
        /// </summary>
        /// <param name="a_twfilesystem">A TWAIN structure</param>
        /// <param name="a_szFilesystem">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToFilesystem(ref TW_FILESYSTEM a_twfilesystem, string a_szFilesystem)
        {
            // Init stuff...
            a_twfilesystem = default(TW_FILESYSTEM);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szFilesystem);

                // Grab the values...
                a_twfilesystem.InputName.Set(asz[0]);
                a_twfilesystem.OutputName.Set(asz[1]);
                a_twfilesystem.Context = (IntPtr)UInt64.Parse(asz[2]);
                a_twfilesystem.Recursive = int.Parse(asz[3]);
                a_twfilesystem.FileType = int.Parse(asz[4]);
                a_twfilesystem.Size = uint.Parse(asz[5]);
                a_twfilesystem.CreateTimeDate.Set(asz[6]);
                a_twfilesystem.ModifiedTimeDate.Set(asz[7]);
                a_twfilesystem.FreeSpace = (uint)UInt64.Parse(asz[8]);
                a_twfilesystem.NewImageSize = (uint)UInt64.Parse(asz[9]);
                a_twfilesystem.NumberOfFiles = (uint)UInt64.Parse(asz[10]);
                a_twfilesystem.NumberOfSnippets = (uint)UInt64.Parse(asz[11]);
                a_twfilesystem.DeviceGroupMask = (uint)UInt64.Parse(asz[12]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of an identity to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twidentity">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string IdentityToCsv(TW_IDENTITY a_twidentity)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twidentity.Id.ToString());
                csv.Add(a_twidentity.Version.MajorNum.ToString());
                csv.Add(a_twidentity.Version.MinorNum.ToString());
                csv.Add(a_twidentity.Version.Language.ToString());
                csv.Add(a_twidentity.Version.Country.ToString());
                csv.Add(a_twidentity.Version.Info.Get());
                csv.Add(a_twidentity.ProtocolMajor.ToString());
                csv.Add(a_twidentity.ProtocolMinor.ToString());
                csv.Add("0x" + a_twidentity.SupportedGroups.ToString("X"));
                csv.Add(a_twidentity.Manufacturer.Get());
                csv.Add(a_twidentity.ProductFamily.Get());
                csv.Add(a_twidentity.ProductName.Get());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to an identity structure...
        /// </summary>
        /// <param name="a_twidentity">A TWAIN structure</param>
        /// <param name="a_szIdentity">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToIdentity(ref TW_IDENTITY a_twidentity, string a_szIdentity)
        {
            // Init stuff...
            a_twidentity = default(TW_IDENTITY);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szIdentity);

                // Grab the values...
                a_twidentity.Id = ulong.Parse(asz[0]);
                a_twidentity.Version.MajorNum = ushort.Parse(asz[1]);
                a_twidentity.Version.MinorNum = ushort.Parse(asz[2]);
                a_twidentity.Version.Language = (TWLG)Enum.Parse(typeof(TWLG), asz[3]);
                a_twidentity.Version.Country = (TWCY)Enum.Parse(typeof(TWCY), asz[4]);
                a_twidentity.Version.Info.Set(asz[5]);
                a_twidentity.ProtocolMajor = ushort.Parse(asz[6]);
                a_twidentity.ProtocolMinor = ushort.Parse(asz[7]);
                a_twidentity.SupportedGroups = asz[8].ToLower().StartsWith("0x") ? Convert.ToUInt32(asz[8].Remove(0, 2), 16) : Convert.ToUInt32(asz[8], 16);
                a_twidentity.Manufacturer.Set(asz[9]);
                a_twidentity.ProductFamily.Set(asz[10]);
                a_twidentity.ProductName.Set(asz[11]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a image info to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twimageinfo">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string ImageinfoToCsv(TW_IMAGEINFO a_twimageinfo)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(((double)a_twimageinfo.XResolution.Whole + ((double)a_twimageinfo.XResolution.Frac / 65536.0)).ToString());
                csv.Add(((double)a_twimageinfo.YResolution.Whole + ((double)a_twimageinfo.YResolution.Frac / 65536.0)).ToString());
                csv.Add(a_twimageinfo.ImageWidth.ToString());
                csv.Add(a_twimageinfo.ImageLength.ToString());
                csv.Add(a_twimageinfo.SamplesPerPixel.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_0.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_1.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_2.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_3.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_4.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_5.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_6.ToString());
                csv.Add(a_twimageinfo.BitsPerSample_7.ToString());
                csv.Add(a_twimageinfo.Planar.ToString());
                csv.Add("TWPT_" + (TWPT)a_twimageinfo.PixelType);
                csv.Add("TWCP_" + (TWCP)a_twimageinfo.Compression);
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to an callback structure...
        /// </summary>
        /// <param name="a_twimageinfo">A TWAIN structure</param>
        /// <param name="a_szImageinfo">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToImageinfo(ref TW_IMAGEINFO a_twimageinfo, string a_szImageinfo)
        {
            // Init stuff...
            a_twimageinfo = default(TW_IMAGEINFO);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szImageinfo);

                // Grab the values...
                a_twimageinfo.XResolution.Whole = (short)double.Parse(asz[0]);
                a_twimageinfo.XResolution.Frac = (ushort)((double.Parse(asz[0]) - (double)a_twimageinfo.XResolution.Whole) * 65536.0);
                a_twimageinfo.YResolution.Whole = (short)double.Parse(asz[1]);
                a_twimageinfo.YResolution.Frac = (ushort)((double.Parse(asz[1]) - (double)a_twimageinfo.YResolution.Whole) * 65536.0);
                a_twimageinfo.ImageWidth = (short)double.Parse(asz[2]);
                a_twimageinfo.ImageLength = int.Parse(asz[3]);
                a_twimageinfo.SamplesPerPixel = short.Parse(asz[4]);
                a_twimageinfo.BitsPerSample_0 = short.Parse(asz[5]);
                a_twimageinfo.BitsPerSample_1 = short.Parse(asz[6]);
                a_twimageinfo.BitsPerSample_2 = short.Parse(asz[7]);
                a_twimageinfo.BitsPerSample_3 = short.Parse(asz[8]);
                a_twimageinfo.BitsPerSample_4 = short.Parse(asz[9]);
                a_twimageinfo.BitsPerSample_5 = short.Parse(asz[10]);
                a_twimageinfo.BitsPerSample_6 = short.Parse(asz[11]);
                a_twimageinfo.BitsPerSample_7 = short.Parse(asz[12]);
                a_twimageinfo.Planar = ushort.Parse(asz[13]);
                a_twimageinfo.PixelType = (short)(TWPT)Enum.Parse(typeof(TWPT), asz[14].Remove(0, 5));
                a_twimageinfo.Compression = (ushort)(TWCP)Enum.Parse(typeof(TWCP), asz[15].Remove(0, 5));
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a image layout to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twimagelayout">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string ImagelayoutToCsv(TW_IMAGELAYOUT a_twimagelayout)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(((double)a_twimagelayout.Frame.Left.Whole + ((double)a_twimagelayout.Frame.Left.Frac / 65536.0)).ToString());
                csv.Add(((double)a_twimagelayout.Frame.Top.Whole + ((double)a_twimagelayout.Frame.Top.Frac / 65536.0)).ToString());
                csv.Add(((double)a_twimagelayout.Frame.Right.Whole + ((double)a_twimagelayout.Frame.Right.Frac / 65536.0)).ToString());
                csv.Add(((double)a_twimagelayout.Frame.Bottom.Whole + ((double)a_twimagelayout.Frame.Bottom.Frac / 65536.0)).ToString());
                csv.Add(a_twimagelayout.DocumentNumber.ToString());
                csv.Add(a_twimagelayout.PageNumber.ToString());
                csv.Add(a_twimagelayout.FrameNumber.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to an image layout structure...
        /// </summary>
        /// <param name="a_twimagelayout">A TWAIN structure</param>
        /// <param name="a_szImagelayout">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToImagelayout(ref TW_IMAGELAYOUT a_twimagelayout, string a_szImagelayout)
        {
            // Init stuff...
            a_twimagelayout = default(TW_IMAGELAYOUT);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szImagelayout);

                // Sort out the frame...
                a_twimagelayout.Frame.Left.Whole = (short)double.Parse(asz[0]);
                a_twimagelayout.Frame.Left.Frac = (ushort)((double.Parse(asz[0]) - (double)a_twimagelayout.Frame.Left.Whole) * 65536.0);
                a_twimagelayout.Frame.Top.Whole = (short)double.Parse(asz[1]);
                a_twimagelayout.Frame.Top.Frac = (ushort)((double.Parse(asz[1]) - (double)a_twimagelayout.Frame.Top.Whole) * 65536.0);
                a_twimagelayout.Frame.Right.Whole = (short)double.Parse(asz[2]);
                a_twimagelayout.Frame.Right.Frac = (ushort)((double.Parse(asz[2]) - (double)a_twimagelayout.Frame.Right.Whole) * 65536.0);
                a_twimagelayout.Frame.Bottom.Whole = (short)double.Parse(asz[3]);
                a_twimagelayout.Frame.Bottom.Frac = (ushort)((double.Parse(asz[3]) - (double)a_twimagelayout.Frame.Bottom.Whole) * 65536.0);

                // And now the counters...
                a_twimagelayout.DocumentNumber = (uint)int.Parse(asz[4]);
                a_twimagelayout.PageNumber = (uint)int.Parse(asz[5]);
                a_twimagelayout.FrameNumber = (uint)int.Parse(asz[6]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of an image mem xfer structure to a string that
        /// we can show in our simple GUI...
        /// </summary>
        /// <param name="a_twsetupfilexfer">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string ImagememferToCsv(TW_IMAGEMEMXFER a_twimagememxfer)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add("TWCP_" + (TWCP)a_twimagememxfer.Compression);
                csv.Add(a_twimagememxfer.BytesPerRow.ToString());
                csv.Add(a_twimagememxfer.Columns.ToString());
                csv.Add(a_twimagememxfer.Rows.ToString());
                csv.Add(a_twimagememxfer.XOffset.ToString());
                csv.Add(a_twimagememxfer.YOffset.ToString());
                csv.Add(a_twimagememxfer.BytesWritten.ToString());
                csv.Add(a_twimagememxfer.Memory.Flags.ToString());
                csv.Add(a_twimagememxfer.Memory.Length.ToString());
                csv.Add(a_twimagememxfer.Memory.TheMem.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a pending xfers structure to a string that
        /// we can show in our simple GUI...
        /// </summary>
        /// <param name="a_twsetupfilexfer">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string PendingxfersToCsv(TW_PENDINGXFERS a_twpendingxfers)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twpendingxfers.Count.ToString());
                csv.Add(a_twpendingxfers.EOJ.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a setup file xfer structure to a string that
        /// we can show in our simple GUI...
        /// </summary>
        /// <param name="a_twsetupfilexfer">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string SetupfilexferToCsv(TW_SETUPFILEXFER a_twsetupfilexfer)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twsetupfilexfer.FileName.Get());
                csv.Add("TWFF_" + a_twsetupfilexfer.Format);
                csv.Add(a_twsetupfilexfer.VRefNum.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert a string to a setupfilexfer...
        /// </summary>
        /// <param name="a_twsetupfilexfer">A TWAIN structure</param>
        /// <param name="a_szSetupfilexfer">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToSetupfilexfer(ref TW_SETUPFILEXFER a_twsetupfilexfer, string a_szSetupfilexfer)
        {
            // Init stuff...
            a_twsetupfilexfer = default(TW_SETUPFILEXFER);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szSetupfilexfer);

                // Sort out the values...
                a_twsetupfilexfer.FileName.Set(asz[0]);
                a_twsetupfilexfer.Format = (TWFF)Enum.Parse(typeof(TWFF), asz[1].Remove(0, 5));
                a_twsetupfilexfer.VRefNum = short.Parse(asz[2]);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a setup mem xfer structure to a string that
        /// we can show in our simple GUI...
        /// </summary>
        /// <param name="a_twsetupmemxfer">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string SetupmemxferToCsv(TW_SETUPMEMXFER a_twsetupmemxfer)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twsetupmemxfer.MinBufSize.ToString());
                csv.Add(a_twsetupmemxfer.MaxBufSize.ToString());
                csv.Add(a_twsetupmemxfer.Preferred.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a userinterface to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twuserinterface">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string UserinterfaceToCsv(TW_USERINTERFACE a_twuserinterface)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add(a_twuserinterface.ShowUI.ToString());
                csv.Add(a_twuserinterface.ModalUI.ToString());
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert a string to a userinterface...
        /// </summary>
        /// <param name="a_twuserinterface">A TWAIN structure</param>
        /// <param name="a_szUserinterface">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToUserinterface(ref TW_USERINTERFACE a_twuserinterface, string a_szUserinterface)
        {
            // Init stuff...
            a_twuserinterface = default(TW_USERINTERFACE);

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szUserinterface);

                // Init stuff...
                a_twuserinterface.ShowUI = 0;
                a_twuserinterface.ModalUI = 0;
                a_twuserinterface.hParent = IntPtr.Zero;

                // Sort out the values...
                if (asz.Length >= 1)
                {
                    ushort.TryParse(asz[0], out a_twuserinterface.ShowUI);
                }
                if (asz.Length >= 2)
                {
                    ushort.TryParse(asz[1], out a_twuserinterface.ModalUI);
                }
                if (asz.Length >= 3)
                {
                    Int64 i64;
                    if (Int64.TryParse(asz[2], out i64))
                    {
                        a_twuserinterface.hParent = (IntPtr)i64;
                    }
                }
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        /// <summary>
        /// Convert the contents of a transfer group to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_u32Xfergroup">A TWAIN structure</param>
        /// <returns>A CSV string of the TWAIN structure</returns>
        public string XfergroupToCsv(UInt32 a_u32Xfergroup)
        {
            try
            {
                CSV csv = new CSV();
                csv.Add("0x" + a_u32Xfergroup.ToString("X"));
                return (csv.Get());
            }
            catch
            {
                return ("***error***");
            }
        }

        /// <summary>
        /// Convert the contents of a string to a transfer group...
        /// </summary>
        /// <param name="a_twcustomdsdata">A TWAIN structure</param>
        /// <param name="a_szCustomdsdata">A CSV string of the TWAIN structure</param>
        /// <returns>True if the conversion is successful</returns>
        public bool CsvToXfergroup(ref UInt32 a_u32Xfergroup, string a_szXfergroup)
        {
            // Init stuff...
            a_u32Xfergroup = 0;

            // Build the string...
            try
            {
                string[] asz = CSV.Parse(a_szXfergroup);

                // Grab the values...
                a_u32Xfergroup = asz[0].ToLower().StartsWith("0x") ? Convert.ToUInt32(asz[0].Remove(0, 2), 16) : Convert.ToUInt32(asz[0], 16);
            }
            catch
            {
                return (false);
            }

            // All done...
            return (true);
        }

        #endregion Public Helper Functions...

        ///////////////////////////////////////////////////////////////////////////////
        // Public DSM_Entry calls, most of the DSM_Entry calls are in here.  Their
        // main contribution is to make sure that they're running within the right
        // thread, but they also include the state transitions...
        ///////////////////////////////////////////////////////////////////////////////

        #region Public DSM_Entry calls...

        /// <summary>
        /// Generic DSM when the dest must be zero (null)...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_dat">Data argument type</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twmemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        public STS DsmEntryNullDest(DG a_dg, DAT a_dat, MSG a_msg, IntPtr a_twmemref)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twmemref = a_twmemref;
                    threaddata.msg = a_msg;
                    threaddata.dg = a_dg;
                    threaddata.dat = a_dat;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twmemref = m_twaincommand.Get(lIndex).twmemref;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), a_dat.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryNullDest(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, a_dat, a_msg, a_twmemref);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryNullDest(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, a_dat, a_msg, a_twmemref);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryNullDest(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, a_dat, a_msg, a_twmemref);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryNullDest(ref m_twidentityApp, IntPtr.Zero, a_dg, a_dat, a_msg, a_twmemref);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryNullDest(ref m_twidentitymacosxApp, IntPtr.Zero, a_dg, a_dat, a_msg, a_twmemref);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Generic DSM when the dest must be a data source...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_dat">Data argument type</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twmemref">Pointer to data</param>
        /// <returns>TWAIN status</returns>
        public STS DsmEntry(DG a_dg, DAT a_dat, MSG a_msg, IntPtr a_twmemref)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twmemref = a_twmemref;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = a_dat;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twmemref = m_twaincommand.Get(lIndex).twmemref;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), a_dat.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntry(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, a_dat, a_msg, a_twmemref);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntry(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, a_dat, a_msg, a_twmemref);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntry(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, a_dat, a_msg, a_twmemref);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntry(ref m_twidentityApp, ref m_twidentityDs, a_dg, a_dat, a_msg, a_twmemref);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntry(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.AUDIOINFO, a_msg, a_twmemref);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set audio info information...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twaudioinfo">Audio structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatAudioinfo(DG a_dg, MSG a_msg, ref TW_AUDIOINFO a_twaudioinfo)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twaudioinfo = a_twaudioinfo;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.AUDIOINFO;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twaudioinfo = m_twaincommand.Get(lIndex).twaudioinfo;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.AUDIOINFO.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryAudioAudioinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.AUDIOINFO, a_msg, ref a_twaudioinfo);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryAudioAudioinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.AUDIOINFO, a_msg, ref a_twaudioinfo);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryAudioAudioinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.AUDIOINFO, a_msg, ref a_twaudioinfo);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryAudioAudioinfo(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.AUDIOINFO, a_msg, ref a_twaudioinfo);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryAudioAudioinfo(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.AUDIOINFO, a_msg, ref a_twaudioinfo);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue callback commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twcallback">Callback structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatCallback(DG a_dg, MSG a_msg, ref TW_CALLBACK a_twcallback)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twcallback = a_twcallback;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.CALLBACK;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twcallback = m_twaincommand.Get(lIndex).twcallback;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.CALLBACK.ToString(), a_msg.ToString(), CallbackToCsv(a_twcallback));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK, a_msg, ref a_twcallback);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK, a_msg, ref a_twcallback);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK, a_msg, ref a_twcallback);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryCallback(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.CALLBACK, a_msg, ref a_twcallback);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryCallback(ref m_twidentitymacosxApp, IntPtr.Zero, a_dg, DAT.CALLBACK, a_msg, ref a_twcallback);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), CallbackToCsv(a_twcallback));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue callback2 commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twcallback2">Callback2 structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatCallback2(DG a_dg, MSG a_msg, ref TW_CALLBACK2 a_twcallback2)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twcallback2 = a_twcallback2;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.CALLBACK;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twcallback2 = m_twaincommand.Get(lIndex).twcallback2;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.CALLBACK2.ToString(), a_msg.ToString(), Callback2ToCsv(a_twcallback2));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryCallback2(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK2, a_msg, ref a_twcallback2);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryCallback2(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK2, a_msg, ref a_twcallback2);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryCallback2(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CALLBACK2, a_msg, ref a_twcallback2);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryCallback2(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.CALLBACK2, a_msg, ref a_twcallback2);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryCallback2(ref m_twidentitymacosxApp, IntPtr.Zero, a_dg, DAT.CALLBACK2, a_msg, ref a_twcallback2);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), Callback2ToCsv(a_twcallback2));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue capabilities commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twcapability">CAPABILITY structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatCapability(DG a_dg, MSG a_msg, ref TW_CAPABILITY a_twcapability)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    ThreadData threaddata = default(ThreadData);
                    long lIndex = 0;

                    // TBD: sometimes this doesn't work!  Not sure why
                    // yet, but a retry takes care of it.
                    for (int ii = 0; ii < 5; ii++)
                    {
                        // Set our command variables...
                        threaddata = default(ThreadData);
                        threaddata.twcapability = a_twcapability;
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.CAPABILITY;
                        lIndex = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Hmmm...
                        if ((a_msg == MSG.GETCURRENT)
                            && (m_twaincommand.Get(lIndex).sts == STS.SUCCESS)
                            && (m_twaincommand.Get(lIndex).twcapability.ConType == (TWON)0)
                            && (m_twaincommand.Get(lIndex).twcapability.hContainer == IntPtr.Zero))
                        {
                            Thread.Sleep(1000);
                            continue;
                        }

                        // We're done...
                        break;
                    }

                    // Return the result...
                    a_twcapability = m_twaincommand.Get(lIndex).twcapability;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                if ((a_msg == MSG.SET) || (a_msg == MSG.SETCONSTRAINT))
                {
                    Log.LogSendBefore(a_dg.ToString(), DAT.CAPABILITY.ToString(), a_msg.ToString(), CapabilityToCsv(a_twcapability));
                }
                else
                {
                    string szCap = a_twcapability.Cap.ToString();
                    if (!szCap.Contains("_"))
                    {
                        szCap = "0x" + ((ushort)a_twcapability.Cap).ToString("X");
                    }
                    Log.LogSendBefore(a_dg.ToString(), DAT.CAPABILITY.ToString(), a_msg.ToString(), szCap + ",0,0");
                }
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryCapability(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CAPABILITY, a_msg, ref a_twcapability);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryCapability(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CAPABILITY, a_msg, ref a_twcapability);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryCapability(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CAPABILITY, a_msg, ref a_twcapability);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryCapability(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.CAPABILITY, a_msg, ref a_twcapability);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryCapability(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.CAPABILITY, a_msg, ref a_twcapability);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                if ((a_msg == MSG.RESETALL) || ((sts != STS.SUCCESS) && (sts != STS.CHECKSTATUS)))
                {
                    Log.LogSendAfter(sts.ToString(), "");
                }
                else
                {
                    Log.LogSendAfter(sts.ToString(), CapabilityToCsv(a_twcapability));
                }
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for CIE color...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twceicolor">CIECOLOR structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatCiecolor(DG a_dg, MSG a_msg, ref TW_CIECOLOR a_twciecolor)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twciecolor = a_twciecolor;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.CIECOLOR;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twciecolor = m_twaincommand.Get(lIndex).twciecolor;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.CIECOLOR.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryCiecolor(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CIECOLOR, a_msg, ref a_twciecolor);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryCiecolor(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CIECOLOR, a_msg, ref a_twciecolor);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryCiecolor(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CIECOLOR, a_msg, ref a_twciecolor);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryCiecolor(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.CIECOLOR, a_msg, ref a_twciecolor);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryCiecolor(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.CIECOLOR, a_msg, ref a_twciecolor);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set the custom DS data...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twcustomdsdata">CUSTOMDSDATA structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatCustomdsdata(DG a_dg, MSG a_msg, ref TW_CUSTOMDSDATA a_twcustomdsdata)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twcustomdsdata = a_twcustomdsdata;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.CUSTOMDSDATA;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twcustomdsdata = m_twaincommand.Get(lIndex).twcustomdsdata;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.CUSTOMDSDATA.ToString(), a_msg.ToString(), CustomdsdataToCsv(a_twcustomdsdata));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryCustomdsdata(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CUSTOMDSDATA, a_msg, ref a_twcustomdsdata);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryCustomdsdata(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CUSTOMDSDATA, a_msg, ref a_twcustomdsdata);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryCustomdsdata(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.CUSTOMDSDATA, a_msg, ref a_twcustomdsdata);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryCustomdsdata(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.CUSTOMDSDATA, a_msg, ref a_twcustomdsdata);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryCustomdsdata(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.CUSTOMDSDATA, a_msg, ref a_twcustomdsdata);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), CustomdsdataToCsv(a_twcustomdsdata));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get device events...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twdeviceevent">DEVICEEVENT structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatDeviceevent(DG a_dg, MSG a_msg, ref TW_DEVICEEVENT a_twdeviceevent)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twdeviceevent = a_twdeviceevent;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.DEVICEEVENT;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twdeviceevent = m_twaincommand.Get(lIndex).twdeviceevent;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.DEVICEEVENT.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryDeviceevent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.DEVICEEVENT, a_msg, ref a_twdeviceevent);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryDeviceevent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.DEVICEEVENT, a_msg, ref a_twdeviceevent);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryDeviceevent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.DEVICEEVENT, a_msg, ref a_twdeviceevent);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryDeviceevent(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.DEVICEEVENT, a_msg, ref a_twdeviceevent);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryDeviceevent(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.DEVICEEVENT, a_msg, ref a_twdeviceevent);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), DeviceeventToCsv(a_twdeviceevent));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get the entrypoint data...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twentrypoint">ENTRYPOINT structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatEntrypoint(DG a_dg, MSG a_msg, ref TW_ENTRYPOINT a_twentrypoint)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twentrypoint = a_twentrypoint;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.ENTRYPOINT;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twentrypoint = m_twaincommand.Get(lIndex).twentrypoint;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.ENTRYPOINT.ToString(), a_msg.ToString(), EntrypointToCsv(a_twentrypoint));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryEntrypoint(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ENTRYPOINT, a_msg, ref a_twentrypoint);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryEntrypoint(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ENTRYPOINT, a_msg, ref a_twentrypoint);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryEntrypoint(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ENTRYPOINT, a_msg, ref a_twentrypoint);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryEntrypoint(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.ENTRYPOINT, a_msg, ref a_twentrypoint);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryEntrypoint(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.ENTRYPOINT, a_msg, ref a_twentrypoint);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // If we were successful, then squirrel away the data...
            if (sts == TWAIN.STS.SUCCESS)
            {
                m_twentrypointdelegates = default(TWAIN.TW_ENTRYPOINT_DELEGATES);
                m_twentrypointdelegates.Size = a_twentrypoint.Size;
                m_twentrypointdelegates.DSM_Entry = a_twentrypoint.DSM_Entry;
                if (a_twentrypoint.DSM_MemAllocate != null)
                {
                    m_twentrypointdelegates.DSM_MemAllocate = (TWAIN.DSM_MEMALLOC)Marshal.GetDelegateForFunctionPointer(a_twentrypoint.DSM_MemAllocate, typeof(TWAIN.DSM_MEMALLOC));
                }
                if (a_twentrypoint.DSM_MemFree != null)
                {
                    m_twentrypointdelegates.DSM_MemFree = (TWAIN.DSM_MEMFREE)Marshal.GetDelegateForFunctionPointer(a_twentrypoint.DSM_MemFree, typeof(TWAIN.DSM_MEMFREE));
                }
                if (a_twentrypoint.DSM_MemLock != null)
                {
                    m_twentrypointdelegates.DSM_MemLock = (TWAIN.DSM_MEMLOCK)Marshal.GetDelegateForFunctionPointer(a_twentrypoint.DSM_MemLock, typeof(TWAIN.DSM_MEMLOCK));
                }
                if (a_twentrypoint.DSM_MemUnlock != null)
                {
                    m_twentrypointdelegates.DSM_MemUnlock = (TWAIN.DSM_MEMUNLOCK)Marshal.GetDelegateForFunctionPointer(a_twentrypoint.DSM_MemUnlock, typeof(TWAIN.DSM_MEMUNLOCK));
                }
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), EntrypointToCsv(a_twentrypoint));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue event commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twevent">EVENT structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatEvent(DG a_dg, MSG a_msg, ref TW_EVENT a_twevent)
        {
            STS sts;

            // Log it...
            if (Log.GetLevel() > 1)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.EVENT.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryEvent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EVENT, a_msg, ref a_twevent);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryEvent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EVENT, a_msg, ref a_twevent);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryEvent(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EVENT, a_msg, ref a_twevent);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryEvent(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.EVENT, a_msg, ref a_twevent);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryEvent(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.EVENT, a_msg, ref a_twevent);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 1)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // Check the event for anything interesting...
            if ((sts == STS.DSEVENT) || (sts == STS.NOTDSEVENT))
            {
                ProcessEvent((MSG)a_twevent.TWMessage);
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set extended image info information...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twextimageinfo">EXTIMAGEINFO structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatExtimageinfo(DG a_dg, MSG a_msg, ref TW_EXTIMAGEINFO a_twextimageinfo)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twextimageinfo = a_twextimageinfo;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.EXTIMAGEINFO;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twextimageinfo = m_twaincommand.Get(lIndex).twextimageinfo;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.EXTIMAGEINFO.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryExtimageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EXTIMAGEINFO, a_msg, ref a_twextimageinfo);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryExtimageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EXTIMAGEINFO, a_msg, ref a_twextimageinfo);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryExtimageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.EXTIMAGEINFO, a_msg, ref a_twextimageinfo);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryExtimageinfo(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.EXTIMAGEINFO, a_msg, ref a_twextimageinfo);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryExtimageinfo(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.EXTIMAGEINFO, a_msg, ref a_twextimageinfo);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set the filesystem...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twfilesystem">FILESYSTEM structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatFilesystem(DG a_dg, MSG a_msg, ref TW_FILESYSTEM a_twfilesystem)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twfilesystem = a_twfilesystem;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.FILESYSTEM;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twfilesystem = m_twaincommand.Get(lIndex).twfilesystem;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.FILESYSTEM.ToString(), a_msg.ToString(), FilesystemToCsv(a_twfilesystem));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryFilesystem(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILESYSTEM, a_msg, ref a_twfilesystem);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryFilesystem(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILESYSTEM, a_msg, ref a_twfilesystem);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryFilesystem(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILESYSTEM, a_msg, ref a_twfilesystem);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryFilesystem(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.FILESYSTEM, a_msg, ref a_twfilesystem);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryFilesystem(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.FILESYSTEM, a_msg, ref a_twfilesystem);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), FilesystemToCsv(a_twfilesystem));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set filter information...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twfilter">FILTER structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatFilter(DG a_dg, MSG a_msg, ref TW_FILTER a_twfilter)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twfilter = a_twfilter;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.FILTER;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twfilter = m_twaincommand.Get(lIndex).twfilter;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.FILTER.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryFilter(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILTER, a_msg, ref a_twfilter);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryFilter(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILTER, a_msg, ref a_twfilter);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryFilter(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.FILTER, a_msg, ref a_twfilter);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryFilter(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.FILTER, a_msg, ref a_twfilter);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryFilter(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.FILTER, a_msg, ref a_twfilter);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for Gray response...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twgrayresponse">GRAYRESPONSE structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatGrayresponse(DG a_dg, MSG a_msg, ref TW_GRAYRESPONSE a_twgrayresponse)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twgrayresponse = a_twgrayresponse;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.GRAYRESPONSE;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twgrayresponse = m_twaincommand.Get(lIndex).twgrayresponse;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.GRAYRESPONSE.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryGrayresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.GRAYRESPONSE, a_msg, ref a_twgrayresponse);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryGrayresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.GRAYRESPONSE, a_msg, ref a_twgrayresponse);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryGrayresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.GRAYRESPONSE, a_msg, ref a_twgrayresponse);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryGrayresponse(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.GRAYRESPONSE, a_msg, ref a_twgrayresponse);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryGrayresponse(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.GRAYRESPONSE, a_msg, ref a_twgrayresponse);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set an ICC profile...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twmemory">ICCPROFILE structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatIccprofile(DG a_dg, MSG a_msg, ref TW_MEMORY a_twmemory)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twmemory = a_twmemory;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.ICCPROFILE;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twmemory = m_twaincommand.Get(lIndex).twmemory;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.ICCPROFILE.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryIccprofile(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ICCPROFILE, a_msg, ref a_twmemory);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryIccprofile(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ICCPROFILE, a_msg, ref a_twmemory);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryIccprofile(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.ICCPROFILE, a_msg, ref a_twmemory);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryIccprofile(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.ICCPROFILE, a_msg, ref a_twmemory);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryIccprofile(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.ICCPROFILE, a_msg, ref a_twmemory);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue identity commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twidentity">IDENTITY structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatIdentity(DG a_dg, MSG a_msg, ref TW_IDENTITY a_twidentity)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twidentity = a_twidentity;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.IDENTITY;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twidentity = m_twaincommand.Get(lIndex).twidentity;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IDENTITY.ToString(), a_msg.ToString(), ((a_msg == MSG.OPENDS) ? IdentityToCsv(a_twidentity) : ""));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                TW_IDENTITY_LEGACY twidentitylegacy = TwidentityToTwidentitylegacy(a_twidentity);
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryIdentity(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.IDENTITY, a_msg, ref twidentitylegacy);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryIdentity(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.IDENTITY, a_msg, ref twidentitylegacy);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
                a_twidentity = TwidentitylegacyToTwidentity(twidentitylegacy);
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        TW_IDENTITY_LEGACY twidentitylegacy = TwidentityToTwidentitylegacy(a_twidentity);
                        sts = (STS)LinuxDsmEntryIdentity(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.IDENTITY, a_msg, ref twidentitylegacy);
                        a_twidentity = TwidentitylegacyToTwidentity(twidentitylegacy);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryIdentity(ref m_twidentityApp, IntPtr.Zero, a_dg, DAT.IDENTITY, a_msg, ref a_twidentity);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                TW_IDENTITY_MACOSX twidentitymacosx = TwidentityToTwidentitymacosx(a_twidentity);
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryIdentity(ref m_twidentitymacosxApp, IntPtr.Zero, a_dg, DAT.IDENTITY, a_msg, ref twidentitymacosx);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
                a_twidentity = TwidentitymacosxToTwidentity(twidentitymacosx);
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), IdentityToCsv(a_twidentity));
            }

            // If we opened, go to state 4...
            if (a_msg == MSG.OPENDS)
            {
                if (sts == STS.SUCCESS)
                {
                    // Change our state, and record the identity we picked...
                    m_state = STATE.S4;
                    m_twidentityDs = a_twidentity;
                    m_twidentitylegacyDs = TwidentityToTwidentitylegacy(m_twidentityDs);
                    m_twidentitymacosxDs = TwidentityToTwidentitymacosx(m_twidentityDs);

                    // Register for callbacks...

                    // Windows...
                    if (ms_platform == Platform.WINDOWS)
                    {
                        if (m_blUseCallbacks)
                        {
                            TW_CALLBACK twcallback = new TW_CALLBACK();
                            twcallback.CallBackProc = Marshal.GetFunctionPointerForDelegate(m_windowsdsmentrycontrolcallbackdelegate);
                            // Log it...
                            if (Log.GetLevel() > 0)
                            {
                                Log.LogSendBefore(a_dg.ToString(), DAT.CALLBACK.ToString(), a_msg.ToString(), CallbackToCsv(twcallback));
                            }
                            // Issue the command...
                            try
                            {
                                if (m_blUseLegacyDSM)
                                {
                                    sts = (STS)WindowsTwain32DsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.CALLBACK, MSG.REGISTER_CALLBACK, ref twcallback);
                                }
                                else
                                {
                                    sts = (STS)WindowsTwaindsmDsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.CALLBACK, MSG.REGISTER_CALLBACK, ref twcallback);
                                }
                            }
                            catch
                            {
                                // The driver crashed...
                                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                                return (STS.BUMMER);
                            }
                            // Log it...
                            if (Log.GetLevel() > 0)
                            {
                                Log.LogSendAfter(sts.ToString(), "");
                            }
                        }
                    }

                    // Linux...
                    else if (ms_platform == Platform.LINUX)
                    {
                        TW_CALLBACK twcallback = new TW_CALLBACK();
                        twcallback.CallBackProc = Marshal.GetFunctionPointerForDelegate(m_linuxdsmentrycontrolcallbackdelegate);
                        // Log it...
                        if (Log.GetLevel() > 0)
                        {
                            Log.LogSendBefore(a_dg.ToString(), DAT.CALLBACK.ToString(), MSG.REGISTER_CALLBACK.ToString(), CallbackToCsv(twcallback));
                        }
                        // Issue the command...
                        try
                        {
                            if (TWAIN.GetMachineWordBitSize() == 32)
                            {
                                sts = (STS)LinuxDsmEntryCallback(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.CALLBACK, MSG.REGISTER_CALLBACK, ref twcallback);
                            }
                            else
                            {
                                sts = (STS)Linux64DsmEntryCallback(ref m_twidentityApp, ref m_twidentityDs, DG.CONTROL, DAT.CALLBACK, MSG.REGISTER_CALLBACK, ref twcallback);
                            }
                        }
                        catch
                        {
                            // The driver crashed...
                            Log.LogSendAfter(STS.BUMMER.ToString(), "");
                            return (STS.BUMMER);
                        }
                        // Log it...
                        if (Log.GetLevel() > 0)
                        {
                            Log.LogSendAfter(sts.ToString(), "");
                        }
                    }

                    // Mac OS X, which has to be different...
                    else if (ms_platform == Platform.MACOSX)
                    {
                        IntPtr intptr = IntPtr.Zero;
                        TW_CALLBACK twcallback = new TW_CALLBACK();
                        twcallback.CallBackProc = Marshal.GetFunctionPointerForDelegate(m_macosxdsmentrycontrolcallbackdelegate);
                        // Log it...
                        if (Log.GetLevel() > 0)
                        {
                            Log.LogSendBefore(a_dg.ToString(), DAT.CALLBACK.ToString(), a_msg.ToString(), CallbackToCsv(twcallback));
                        }
                        // Issue the command...
                        try
                        {
                            sts = (STS)MacosxDsmEntryCallback(ref m_twidentitymacosxApp, intptr, DG.CONTROL, DAT.CALLBACK, MSG.REGISTER_CALLBACK, ref twcallback);
                        }
                        catch
                        {
                            // The driver crashed...
                            Log.LogSendAfter(STS.BUMMER.ToString(), "");
                            return (STS.BUMMER);
                        }
                        // Log it...
                        if (Log.GetLevel() > 0)
                        {
                            Log.LogSendAfter(sts.ToString(), "");
                        }
                    }
                }
            }

            // If we closed, go to state 3...
            else if (a_msg == MSG.CLOSEDS)
            {
                if (sts == STS.SUCCESS)
                {
                    m_state = STATE.S3;
                }
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set image info information...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twimageinfo">IMAGEINFO structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatImageinfo(DG a_dg, MSG a_msg, ref TW_IMAGEINFO a_twimageinfo)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twimageinfo = a_twimageinfo;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.IMAGEINFO;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twimageinfo = m_twaincommand.Get(lIndex).twimageinfo;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGEINFO.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryImageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEINFO, a_msg, ref a_twimageinfo);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryImageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEINFO, a_msg, ref a_twimageinfo);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImageinfo(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEINFO, a_msg, ref a_twimageinfo);
                    }
                    else
                    {
                        TW_IMAGEINFO_LINUX64 twimageinfolinux64 = default(TW_IMAGEINFO_LINUX64);
                        sts = (STS)Linux64DsmEntryImageinfo(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGEINFO, a_msg, ref twimageinfolinux64);
                        a_twimageinfo.XResolution = twimageinfolinux64.XResolution;
                        a_twimageinfo.YResolution = twimageinfolinux64.YResolution;
                        a_twimageinfo.ImageWidth = (int)twimageinfolinux64.ImageWidth;
                        a_twimageinfo.ImageLength = (int)twimageinfolinux64.ImageLength;
                        a_twimageinfo.SamplesPerPixel = twimageinfolinux64.SamplesPerPixel;
                        a_twimageinfo.BitsPerSample_0 = twimageinfolinux64.BitsPerSample_0;
                        a_twimageinfo.BitsPerSample_1 = twimageinfolinux64.BitsPerSample_1;
                        a_twimageinfo.BitsPerSample_2 = twimageinfolinux64.BitsPerSample_2;
                        a_twimageinfo.BitsPerSample_3 = twimageinfolinux64.BitsPerSample_3;
                        a_twimageinfo.BitsPerSample_4 = twimageinfolinux64.BitsPerSample_4;
                        a_twimageinfo.BitsPerSample_5 = twimageinfolinux64.BitsPerSample_5;
                        a_twimageinfo.BitsPerSample_6 = twimageinfolinux64.BitsPerSample_6;
                        a_twimageinfo.BitsPerSample_7 = twimageinfolinux64.BitsPerSample_7;
                        a_twimageinfo.BitsPerPixel = twimageinfolinux64.BitsPerPixel;
                        a_twimageinfo.Planar = twimageinfolinux64.Planar;
                        a_twimageinfo.PixelType = twimageinfolinux64.PixelType;
                        a_twimageinfo.Compression = twimageinfolinux64.Compression;
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryImageinfo(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGEINFO, a_msg, ref a_twimageinfo);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), ImageinfoToCsv(a_twimageinfo));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set layout information...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twimagelayout">IMAGELAYOUT structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatImagelayout(DG a_dg, MSG a_msg, ref TW_IMAGELAYOUT a_twimagelayout)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twimagelayout = a_twimagelayout;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.IMAGELAYOUT;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twimagelayout = m_twaincommand.Get(lIndex).twimagelayout;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGELAYOUT.ToString(), a_msg.ToString(), ImagelayoutToCsv(a_twimagelayout));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryImagelayout(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGELAYOUT, a_msg, ref a_twimagelayout);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryImagelayout(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGELAYOUT, a_msg, ref a_twimagelayout);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImagelayout(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGELAYOUT, a_msg, ref a_twimagelayout);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryImagelayout(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGELAYOUT, a_msg, ref a_twimagelayout);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryImagelayout(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGELAYOUT, a_msg, ref a_twimagelayout);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), ImagelayoutToCsv(a_twimagelayout));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue file image transfer commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <returns>TWAIN status</returns>
        private void DatImagefilexferWindowsTwain32()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagefilexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwain32DsmEntryImagefilexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                IntPtr.Zero
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagefilexfer, threaddata);
        }

        private void DatImagefilexferWindowsTwainDsm()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagefilexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwaindsmDsmEntryImagefilexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                IntPtr.Zero
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagefilexfer, threaddata);
        }

        public STS DatImagefilexfer(DG a_dg, MSG a_msg)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if (this.m_runinuithreaddelegate == null)
            {
                if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
                {
                    lock (m_lockTwain)
                    {
                        // Set our command variables...
                        ThreadData threaddata = default(ThreadData);
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.IMAGEFILEXFER;
                        long lIndex = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Return the result...
                        sts = m_twaincommand.Get(lIndex).sts;

                        // Clear the command variables...
                        m_twaincommand.Delete(lIndex);
                    }
                    return (sts);
                }
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGEFILEXFER.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (this.m_runinuithreaddelegate == null)
                    {
                        if (m_blUseLegacyDSM)
                        {
                            sts = (STS)WindowsTwain32DsmEntryImagefilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEFILEXFER, a_msg, IntPtr.Zero);
                        }
                        else
                        {
                            sts = (STS)WindowsTwaindsmDsmEntryImagefilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEFILEXFER, a_msg, IntPtr.Zero);
                        }
                    }
                    else
                    {
                        if (m_blUseLegacyDSM)
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEFILEXFER;
                                m_lIndexDatImagefilexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagefilexferWindowsTwain32);
                                sts = m_twaincommand.Get(m_lIndexDatImagefilexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagefilexfer);
                            }
                        }
                        else
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEFILEXFER;
                                m_lIndexDatImagefilexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagefilexferWindowsTwainDsm);
                                sts = m_twaincommand.Get(m_lIndexDatImagefilexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagefilexfer);
                            }
                        }
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImagefilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEFILEXFER, a_msg, IntPtr.Zero);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryImagefilexfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGEFILEXFER, a_msg, IntPtr.Zero);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryImagefilexfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGEFILEXFER, a_msg, IntPtr.Zero);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // If we had a successful transfer, then change state...
            if (sts == STS.XFERDONE)
            {
                m_state = STATE.S7;
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue memory file image transfer commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twimagememxfer">IMAGEMEMXFER structure</param>
        /// <returns>TWAIN status</returns>
        private void DatImagememfilexferWindowsTwain32()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagememfilexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwain32DsmEntryImagememfilexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twimagememxfer
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagememfilexfer, threaddata);
        }

        private void DatImagememfilexferWindowsTwainDsm()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagememfilexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwaindsmDsmEntryImagememfilexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twimagememxfer
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagememfilexfer, threaddata);
        }

        public STS DatImagememfilexfer(DG a_dg, MSG a_msg, ref TW_IMAGEMEMXFER a_twimagememxfer)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if (this.m_runinuithreaddelegate == null)
            {
                if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
                {
                    lock (m_lockTwain)
                    {
                        // Set our command variables...
                        ThreadData threaddata = default(ThreadData);
                        threaddata.twimagememxfer = a_twimagememxfer;
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.IMAGEMEMFILEXFER;
                        long lIndex = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Return the result...
                        a_twimagememxfer = m_twaincommand.Get(lIndex).twimagememxfer;
                        sts = m_twaincommand.Get(lIndex).sts;

                        // Clear the command variables...
                        m_twaincommand.Delete(lIndex);
                    }
                    return (sts);
                }
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGEMEMFILEXFER.ToString(), a_msg.ToString(), ImagememferToCsv(a_twimagememxfer));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (this.m_runinuithreaddelegate == null)
                    {
                        if (m_blUseLegacyDSM)
                        {
                            sts = (STS)WindowsTwain32DsmEntryImagememfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMFILEXFER, a_msg, ref a_twimagememxfer);
                        }
                        else
                        {
                            sts = (STS)WindowsTwaindsmDsmEntryImagememfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMFILEXFER, a_msg, ref a_twimagememxfer);
                        }
                    }
                    else
                    {
                        if (m_blUseLegacyDSM)
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twimagememxfer = a_twimagememxfer;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEMEMFILEXFER;
                                m_lIndexDatImagememfilexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagememfilexferWindowsTwain32);
                                a_twimagememxfer = m_twaincommand.Get(m_lIndexDatImagememfilexfer).twimagememxfer;
                                sts = m_twaincommand.Get(m_lIndexDatImagememfilexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagememfilexfer);
                            }
                        }
                        else
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twimagememxfer = a_twimagememxfer;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEMEMFILEXFER;
                                m_lIndexDatImagememfilexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagememfilexferWindowsTwainDsm);
                                a_twimagememxfer = m_twaincommand.Get(m_lIndexDatImagememfilexfer).twimagememxfer;
                                sts = m_twaincommand.Get(m_lIndexDatImagememfilexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagememfilexfer);
                            }
                        }
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImagememfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMFILEXFER, a_msg, ref a_twimagememxfer);
                    }
                    else
                    {
                        TW_IMAGEMEMXFER_LINUX64 twimagememxferlinux64 = default(TW_IMAGEMEMXFER_LINUX64);
                        twimagememxferlinux64.BytesPerRow = a_twimagememxfer.BytesPerRow;
                        twimagememxferlinux64.BytesWritten = a_twimagememxfer.BytesWritten;
                        twimagememxferlinux64.Columns = a_twimagememxfer.Columns;
                        twimagememxferlinux64.Compression = a_twimagememxfer.Compression;
                        twimagememxferlinux64.MemoryFlags = a_twimagememxfer.Memory.Flags;
                        twimagememxferlinux64.MemoryLength = a_twimagememxfer.Memory.Length;
                        twimagememxferlinux64.MemoryTheMem = a_twimagememxfer.Memory.TheMem;
                        twimagememxferlinux64.Rows = a_twimagememxfer.Rows;
                        twimagememxferlinux64.XOffset = a_twimagememxfer.XOffset;
                        twimagememxferlinux64.YOffset = a_twimagememxfer.YOffset;
                        sts = (STS)Linux64DsmEntryImagememfilexfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGEMEMFILEXFER, a_msg, ref twimagememxferlinux64);
                        a_twimagememxfer.BytesPerRow = (uint)twimagememxferlinux64.BytesPerRow;
                        a_twimagememxfer.BytesWritten = (uint)twimagememxferlinux64.BytesWritten;
                        a_twimagememxfer.Columns = (uint)twimagememxferlinux64.Columns;
                        a_twimagememxfer.Compression = (ushort)twimagememxferlinux64.Compression;
                        a_twimagememxfer.Memory.Flags = (uint)twimagememxferlinux64.MemoryFlags;
                        a_twimagememxfer.Memory.Length = (uint)twimagememxferlinux64.MemoryLength;
                        a_twimagememxfer.Memory.TheMem = twimagememxferlinux64.MemoryTheMem;
                        a_twimagememxfer.Rows = (uint)twimagememxferlinux64.Rows;
                        a_twimagememxfer.XOffset = (uint)twimagememxferlinux64.XOffset;
                        a_twimagememxfer.YOffset = (uint)twimagememxferlinux64.YOffset;
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    TW_IMAGEMEMXFER_MACOSX twimagememxfermacosx = default(TW_IMAGEMEMXFER_MACOSX);
                    twimagememxfermacosx.BytesPerRow = a_twimagememxfer.BytesPerRow;
                    twimagememxfermacosx.BytesWritten = a_twimagememxfer.BytesWritten;
                    twimagememxfermacosx.Columns = a_twimagememxfer.Columns;
                    twimagememxfermacosx.Compression = a_twimagememxfer.Compression;
                    twimagememxfermacosx.Memory.Flags = a_twimagememxfer.Memory.Flags;
                    twimagememxfermacosx.Memory.Length = a_twimagememxfer.Memory.Length;
                    twimagememxfermacosx.Memory.TheMem = a_twimagememxfer.Memory.TheMem;
                    twimagememxfermacosx.Rows = a_twimagememxfer.Rows;
                    twimagememxfermacosx.XOffset = a_twimagememxfer.XOffset;
                    twimagememxfermacosx.YOffset = a_twimagememxfer.YOffset;
                    sts = (STS)MacosxDsmEntryImagememfilexfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGEMEMFILEXFER, a_msg, ref twimagememxfermacosx);
                    a_twimagememxfer.BytesPerRow = twimagememxfermacosx.BytesPerRow;
                    a_twimagememxfer.BytesWritten = twimagememxfermacosx.BytesWritten;
                    a_twimagememxfer.Columns = twimagememxfermacosx.Columns;
                    a_twimagememxfer.Compression = (ushort)twimagememxfermacosx.Compression;
                    a_twimagememxfer.Memory.Flags = twimagememxfermacosx.Memory.Flags;
                    a_twimagememxfer.Memory.Length = twimagememxfermacosx.Memory.Length;
                    a_twimagememxfer.Memory.TheMem = twimagememxfermacosx.Memory.TheMem;
                    a_twimagememxfer.Rows = twimagememxfermacosx.Rows;
                    a_twimagememxfer.XOffset = twimagememxfermacosx.XOffset;
                    a_twimagememxfer.YOffset = twimagememxfermacosx.YOffset;
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), ImagememferToCsv(a_twimagememxfer));
            }

            // If we had a successful transfer, then change state...
            if (sts == STS.XFERDONE)
            {
                m_state = STATE.S7;
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue memory image transfer commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twimagememxfer">IMAGEMEMXFER structure</param>
        /// <returns>TWAIN status</returns>
        private void DatImagememxferWindowsTwain32()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagememxfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwain32DsmEntryImagememxfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twimagememxfer
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagememxfer, threaddata);
        }

        private void DatImagememxferWindowsTwainDsm()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagememxfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwaindsmDsmEntryImagememxfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twimagememxfer
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagememxfer, threaddata);
        }

        public STS DatImagememxfer(DG a_dg, MSG a_msg, ref TW_IMAGEMEMXFER a_twimagememxfer)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if (this.m_runinuithreaddelegate == null)
            {
                if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
                {
                    lock (m_lockTwain)
                    {
                        // Set our command variables...
                        ThreadData threaddata = default(ThreadData);
                        threaddata.twimagememxfer = a_twimagememxfer;
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.IMAGEMEMXFER;
                        m_lIndexDatImagememxfer = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Return the result...
                        a_twimagememxfer = m_twaincommand.Get(m_lIndexDatImagememxfer).twimagememxfer;
                        sts = m_twaincommand.Get(m_lIndexDatImagememxfer).sts;

                        // Clear the command variables...
                        m_twaincommand.Delete(m_lIndexDatImagememxfer);
                    }
                    return (sts);
                }
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGEMEMXFER.ToString(), a_msg.ToString(), ImagememferToCsv(a_twimagememxfer));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (this.m_runinuithreaddelegate == null)
                    {
                        if (m_blUseLegacyDSM)
                        {
                            sts = (STS)WindowsTwain32DsmEntryImagememxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMXFER, a_msg, ref a_twimagememxfer);
                        }
                        else
                        {
                            sts = (STS)WindowsTwaindsmDsmEntryImagememxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMXFER, a_msg, ref a_twimagememxfer);
                        }
                    }
                    else
                    {
                        if (m_blUseLegacyDSM)
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twimagememxfer = a_twimagememxfer;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEMEMXFER;
                                m_lIndexDatImagememxfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagememxferWindowsTwain32);
                                a_twimagememxfer = m_twaincommand.Get(m_lIndexDatImagememxfer).twimagememxfer;
                                sts = m_twaincommand.Get(m_lIndexDatImagememxfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagememxfer);
                            }
                        }
                        else
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twimagememxfer = a_twimagememxfer;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGEMEMXFER;
                                m_lIndexDatImagememxfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagememxferWindowsTwainDsm);
                                a_twimagememxfer = m_twaincommand.Get(m_lIndexDatImagememxfer).twimagememxfer;
                                sts = m_twaincommand.Get(m_lIndexDatImagememxfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagememxfer);
                            }
                        }
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImagememxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGEMEMXFER, a_msg, ref a_twimagememxfer);
                    }
                    else
                    {
                        TW_IMAGEMEMXFER_LINUX64 twimagememxferlinux64 = default(TW_IMAGEMEMXFER_LINUX64);
                        twimagememxferlinux64.BytesPerRow = a_twimagememxfer.BytesPerRow;
                        twimagememxferlinux64.BytesWritten = a_twimagememxfer.BytesWritten;
                        twimagememxferlinux64.Columns = a_twimagememxfer.Columns;
                        twimagememxferlinux64.Compression = a_twimagememxfer.Compression;
                        twimagememxferlinux64.MemoryFlags = a_twimagememxfer.Memory.Flags;
                        twimagememxferlinux64.MemoryLength = a_twimagememxfer.Memory.Length;
                        twimagememxferlinux64.MemoryTheMem = a_twimagememxfer.Memory.TheMem;
                        twimagememxferlinux64.Rows = a_twimagememxfer.Rows;
                        twimagememxferlinux64.XOffset = a_twimagememxfer.XOffset;
                        twimagememxferlinux64.YOffset = a_twimagememxfer.YOffset;
                        sts = (STS)Linux64DsmEntryImagememxfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGEMEMXFER, a_msg, ref twimagememxferlinux64);
                        a_twimagememxfer.BytesPerRow = (uint)twimagememxferlinux64.BytesPerRow;
                        a_twimagememxfer.BytesWritten = (uint)twimagememxferlinux64.BytesWritten;
                        a_twimagememxfer.Columns = (uint)twimagememxferlinux64.Columns;
                        a_twimagememxfer.Compression = (ushort)twimagememxferlinux64.Compression;
                        a_twimagememxfer.Memory.Flags = (uint)twimagememxferlinux64.MemoryFlags;
                        a_twimagememxfer.Memory.Length = (uint)twimagememxferlinux64.MemoryLength;
                        a_twimagememxfer.Memory.TheMem = twimagememxferlinux64.MemoryTheMem;
                        a_twimagememxfer.Rows = (uint)twimagememxferlinux64.Rows;
                        a_twimagememxfer.XOffset = (uint)twimagememxferlinux64.XOffset;
                        a_twimagememxfer.YOffset = (uint)twimagememxferlinux64.YOffset;
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    TW_IMAGEMEMXFER_MACOSX twimagememxfermacosx = default(TW_IMAGEMEMXFER_MACOSX);
                    twimagememxfermacosx.BytesPerRow = a_twimagememxfer.BytesPerRow;
                    twimagememxfermacosx.BytesWritten = a_twimagememxfer.BytesWritten;
                    twimagememxfermacosx.Columns = a_twimagememxfer.Columns;
                    twimagememxfermacosx.Compression = a_twimagememxfer.Compression;
                    twimagememxfermacosx.Memory.Flags = a_twimagememxfer.Memory.Flags;
                    twimagememxfermacosx.Memory.Length = a_twimagememxfer.Memory.Length;
                    twimagememxfermacosx.Memory.TheMem = a_twimagememxfer.Memory.TheMem;
                    twimagememxfermacosx.Rows = a_twimagememxfer.Rows;
                    twimagememxfermacosx.XOffset = a_twimagememxfer.XOffset;
                    twimagememxfermacosx.YOffset = a_twimagememxfer.YOffset;
                    sts = (STS)MacosxDsmEntryImagememxfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGEMEMXFER, a_msg, ref twimagememxfermacosx);
                    a_twimagememxfer.BytesPerRow = twimagememxfermacosx.BytesPerRow;
                    a_twimagememxfer.BytesWritten = twimagememxfermacosx.BytesWritten;
                    a_twimagememxfer.Columns = twimagememxfermacosx.Columns;
                    a_twimagememxfer.Compression = (ushort)twimagememxfermacosx.Compression;
                    a_twimagememxfer.Memory.Flags = twimagememxfermacosx.Memory.Flags;
                    a_twimagememxfer.Memory.Length = twimagememxfermacosx.Memory.Length;
                    a_twimagememxfer.Memory.TheMem = twimagememxfermacosx.Memory.TheMem;
                    a_twimagememxfer.Rows = twimagememxfermacosx.Rows;
                    a_twimagememxfer.XOffset = twimagememxfermacosx.XOffset;
                    a_twimagememxfer.YOffset = twimagememxfermacosx.YOffset;
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), ImagememferToCsv(a_twimagememxfer));
            }

            // If we had a successful transfer, then change state...
            if (sts == STS.XFERDONE)
            {
                m_state = STATE.S7;
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue native image transfer commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_bitmap">BITMAP structure</param>
        /// <returns>TWAIN status</returns>
        private void DatImagenativexferWindowsTwain32()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagenativexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwain32DsmEntryImagenativexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.intptrBitmap
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagenativexfer, threaddata);
        }

        private void DatImagenativexferWindowsTwainDsm()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatImagenativexfer);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwaindsmDsmEntryImagenativexfer
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.intptrBitmap
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatImagenativexfer, threaddata);
        }

        public STS DatImagenativexfer(DG a_dg, MSG a_msg, ref Image a_bitmap)
        {
            STS sts;
            IntPtr intptrBitmap = IntPtr.Zero;

            // Submit the work to the TWAIN thread...
            if (this.m_runinuithreaddelegate == null)
            {
                if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
                {
                    lock (m_lockTwain)
                    {
                        // Set our command variables...
                        ThreadData threaddata = default(ThreadData);
                        threaddata.bitmap = a_bitmap;
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.IMAGENATIVEXFER;
                        long lIndex = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Return the result...
                        a_bitmap = m_twaincommand.Get(lIndex).bitmap;
                        sts = m_twaincommand.Get(lIndex).sts;

                        // Clear the command variables...
                        m_twaincommand.Delete(lIndex);
                    }
                    return (sts);
                }
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.IMAGENATIVEXFER.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                    }
                    else
                    {
                    }
                    if (this.m_runinuithreaddelegate == null)
                    {
                        if (m_blUseLegacyDSM)
                        {
                            sts = (STS)WindowsTwain32DsmEntryImagenativexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGENATIVEXFER, a_msg, ref intptrBitmap);
                        }
                        else
                        {
                            sts = (STS)WindowsTwaindsmDsmEntryImagenativexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGENATIVEXFER, a_msg, ref intptrBitmap);
                        }
                    }
                    else
                    {
                        if (m_blUseLegacyDSM)
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.intptrBitmap = IntPtr.Zero;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGENATIVEXFER;
                                m_lIndexDatImagenativexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagenativexferWindowsTwain32);
                                intptrBitmap = m_twaincommand.Get(m_lIndexDatImagenativexfer).intptrBitmap;
                                sts = m_twaincommand.Get(m_lIndexDatImagenativexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagenativexfer);
                            }
                        }
                        else
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.intptrBitmap = IntPtr.Zero;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.IMAGENATIVEXFER;
                                m_lIndexDatImagenativexfer = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatImagenativexferWindowsTwainDsm);
                                intptrBitmap = m_twaincommand.Get(m_lIndexDatImagenativexfer).intptrBitmap;
                                sts = m_twaincommand.Get(m_lIndexDatImagenativexfer).sts;
                                m_twaincommand.Delete(m_lIndexDatImagenativexfer);
                            }
                        }
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryImagenativexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.IMAGENATIVEXFER, a_msg, ref intptrBitmap);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryImagenativexfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.IMAGENATIVEXFER, a_msg, ref intptrBitmap);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    intptrBitmap = IntPtr.Zero;
                    sts = (STS)MacosxDsmEntryImagenativexfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.IMAGENATIVEXFER, a_msg, ref intptrBitmap);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // If we had a successful transfer, then convert the data...
            if (sts == STS.XFERDONE)
            {
                // Bump our state...
                m_state = STATE.S7;

                // Turn the DIB into a Bitmap object...
                a_bitmap = NativeToBitmap(ms_platform, intptrBitmap);

                // We're done with the data we got from the driver...
                Marshal.FreeHGlobal(intptrBitmap);
                intptrBitmap = IntPtr.Zero;
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set JPEG compression tables...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twjpegcompression">JPEGCOMPRESSION structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatJpegcompression(DG a_dg, MSG a_msg, ref TW_JPEGCOMPRESSION a_twjpegcompression)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twjpegcompression = a_twjpegcompression;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.JPEGCOMPRESSION;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twjpegcompression = m_twaincommand.Get(lIndex).twjpegcompression;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.JPEGCOMPRESSION.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryJpegcompression(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.JPEGCOMPRESSION, a_msg, ref a_twjpegcompression);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryJpegcompression(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.JPEGCOMPRESSION, a_msg, ref a_twjpegcompression);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryJpegcompression(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.JPEGCOMPRESSION, a_msg, ref a_twjpegcompression);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryJpegcompression(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.JPEGCOMPRESSION, a_msg, ref a_twjpegcompression);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryJpegcompression(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.JPEGCOMPRESSION, a_msg, ref a_twjpegcompression);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for a Pallete8...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twpalette8">PALETTE8 structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatPalette8(DG a_dg, MSG a_msg, ref TW_PALETTE8 a_twpalette8)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twpalette8 = a_twpalette8;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.PALETTE8;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twpalette8 = m_twaincommand.Get(lIndex).twpalette8;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.PALETTE8.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryPalette8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PALETTE8, a_msg, ref a_twpalette8);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryPalette8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PALETTE8, a_msg, ref a_twpalette8);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (TWAIN.GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryPalette8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PALETTE8, a_msg, ref a_twpalette8);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryPalette8(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.PALETTE8, a_msg, ref a_twpalette8);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryPalette8(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.PALETTE8, a_msg, ref a_twpalette8);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue DSM commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_intptrHwnd">PARENT structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatParent(DG a_dg, MSG a_msg, ref IntPtr a_intptrHwnd)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.intptrHwnd = a_intptrHwnd;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.PARENT;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_intptrHwnd = m_twaincommand.Get(lIndex).intptrHwnd;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.PARENT.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryParent(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.PARENT, a_msg, ref a_intptrHwnd);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryParent(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.PARENT, a_msg, ref a_intptrHwnd);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryParent(ref m_twidentitylegacyApp, IntPtr.Zero, a_dg, DAT.PARENT, a_msg, ref a_intptrHwnd);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryParent(ref m_twidentityApp, IntPtr.Zero, a_dg, DAT.PARENT, a_msg, ref a_intptrHwnd);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryParent(ref m_twidentitymacosxApp, IntPtr.Zero, a_dg, DAT.PARENT, a_msg, ref a_intptrHwnd);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // If we opened, go to state 3, and start tracking
            // TWAIN's state in the log file...
            if (a_msg == MSG.OPENDSM)
            {
                if (sts == STS.SUCCESS)
                {
                    m_state = STATE.S3;
                    Log.RegisterTwain(this);
                }
            }

            // If we closed, go to state 2, and stop tracking
            // TWAIN's state in the log file...
            else if (a_msg == MSG.CLOSEDSM)
            {
                if (sts == STS.SUCCESS)
                {
                    m_state = STATE.S2;
                    Log.RegisterTwain(null);
                }
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for a raw commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twpassthru">PASSTHRU structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatPassthru(DG a_dg, MSG a_msg, ref TW_PASSTHRU a_twpassthru)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twpassthru = a_twpassthru;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.PASSTHRU;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twpassthru = m_twaincommand.Get(lIndex).twpassthru;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.PASSTHRU.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryPassthru(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PASSTHRU, a_msg, ref a_twpassthru);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryPassthru(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PASSTHRU, a_msg, ref a_twpassthru);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryPassthru(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PASSTHRU, a_msg, ref a_twpassthru);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryPassthru(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.PASSTHRU, a_msg, ref a_twpassthru);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryPassthru(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.PASSTHRU, a_msg, ref a_twpassthru);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue pendingxfers commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twpendingxfers">PENDINGXFERS structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatPendingxfers(DG a_dg, MSG a_msg, ref TW_PENDINGXFERS a_twpendingxfers)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twpendingxfers = a_twpendingxfers;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.PENDINGXFERS;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twpendingxfers = m_twaincommand.Get(lIndex).twpendingxfers;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.PENDINGXFERS.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryPendingxfers(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PENDINGXFERS, a_msg, ref a_twpendingxfers);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryPendingxfers(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PENDINGXFERS, a_msg, ref a_twpendingxfers);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryPendingxfers(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.PENDINGXFERS, a_msg, ref a_twpendingxfers);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryPendingxfers(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.PENDINGXFERS, a_msg, ref a_twpendingxfers);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryPendingxfers(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.PENDINGXFERS, a_msg, ref a_twpendingxfers);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), PendingxfersToCsv(a_twpendingxfers));
            }

            // If we endxfer, go to state 5 or 6...
            if (a_msg == MSG.ENDXFER)
            {
                if (sts == STS.SUCCESS)
                {
                    if (a_twpendingxfers.Count == 0)
                    {
                        m_blIsMsgxferready = false;
                        m_state = STATE.S5;
                    }
                    else
                    {
                        m_state = STATE.S6;
                    }
                }
            }

            // If we reset, go to state 5...
            else if (a_msg == MSG.RESET)
            {
                if (sts == STS.SUCCESS)
                {
                    m_blIsMsgxferready = false;
                    m_state = STATE.S5;
                }
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for RGB response...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twrgbresponse">RGBRESPONSE structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatRgbresponse(DG a_dg, MSG a_msg, ref TW_RGBRESPONSE a_twrgbresponse)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twrgbresponse = a_twrgbresponse;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.RGBRESPONSE;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twrgbresponse = m_twaincommand.Get(lIndex).twrgbresponse;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.RGBRESPONSE.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryRgbresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.RGBRESPONSE, a_msg, ref a_twrgbresponse);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryRgbresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.RGBRESPONSE, a_msg, ref a_twrgbresponse);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryRgbresponse(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.RGBRESPONSE, a_msg, ref a_twrgbresponse);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryRgbresponse(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.RGBRESPONSE, a_msg, ref a_twrgbresponse);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryRgbresponse(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.RGBRESPONSE, a_msg, ref a_twrgbresponse);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set for a file xfer...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twsetupfilexfer">SETUPFILEXFER structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatSetupfilexfer(DG a_dg, MSG a_msg, ref TW_SETUPFILEXFER a_twsetupfilexfer)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twsetupfilexfer = a_twsetupfilexfer;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.SETUPFILEXFER;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twsetupfilexfer = m_twaincommand.Get(lIndex).twsetupfilexfer;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.SETUPFILEXFER.ToString(), a_msg.ToString(), SetupfilexferToCsv(a_twsetupfilexfer));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntrySetupfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPFILEXFER, a_msg, ref a_twsetupfilexfer);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntrySetupfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPFILEXFER, a_msg, ref a_twsetupfilexfer);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntrySetupfilexfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPFILEXFER, a_msg, ref a_twsetupfilexfer);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntrySetupfilexfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.SETUPFILEXFER, a_msg, ref a_twsetupfilexfer);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntrySetupfilexfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.SETUPFILEXFER, a_msg, ref a_twsetupfilexfer);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), SetupfilexferToCsv(a_twsetupfilexfer));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get info about the memory xfer...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twsetupmemxfer">SETUPMEMXFER structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatSetupmemxfer(DG a_dg, MSG a_msg, ref TW_SETUPMEMXFER a_twsetupmemxfer)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twsetupmemxfer = a_twsetupmemxfer;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.SETUPMEMXFER;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twsetupmemxfer = m_twaincommand.Get(lIndex).twsetupmemxfer;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.SETUPMEMXFER.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntrySetupmemxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPMEMXFER, a_msg, ref a_twsetupmemxfer);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntrySetupmemxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPMEMXFER, a_msg, ref a_twsetupmemxfer);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntrySetupmemxfer(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.SETUPMEMXFER, a_msg, ref a_twsetupmemxfer);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntrySetupmemxfer(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.SETUPMEMXFER, a_msg, ref a_twsetupmemxfer);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntrySetupmemxfer(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.SETUPMEMXFER, a_msg, ref a_twsetupmemxfer);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), SetupmemxferToCsv(a_twsetupmemxfer));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get some text for an error...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twstatusutf8">STATUSUTF8 structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatStatusutf8(DG a_dg, MSG a_msg, ref TW_STATUSUTF8 a_twstatusutf8)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twstatusutf8 = a_twstatusutf8;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.STATUSUTF8;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twstatusutf8 = m_twaincommand.Get(lIndex).twstatusutf8;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.STATUSUTF8.ToString(), a_msg.ToString(), "");
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryStatusutf8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.STATUSUTF8, a_msg, ref a_twstatusutf8);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryStatusutf8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.STATUSUTF8, a_msg, ref a_twstatusutf8);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryStatusutf8(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.STATUSUTF8, a_msg, ref a_twstatusutf8);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryStatusutf8(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.STATUSUTF8, a_msg, ref a_twstatusutf8);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryStatusutf8(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.STATUSUTF8, a_msg, ref a_twstatusutf8);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Issue capabilities commands...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twuserinterface">USERINTERFACE structure</param>
        /// <returns>TWAIN status</returns>
        private void DatUserinterfaceWindowsTwain32()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatUserinterface);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwain32DsmEntryUserinterface
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twuserinterface
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatUserinterface, threaddata);
        }

        private void DatUserinterfaceWindowsTwainDsm()
        {
            ThreadData threaddata = m_twaincommand.Get(m_lIndexDatUserinterface);

            // If you get a first chance exception, be aware that some drivers
            // will do that to you, you can ignore it and they'll keep going...
            threaddata.sts = (STS)WindowsTwaindsmDsmEntryUserinterface
            (
                ref m_twidentitylegacyApp,
                ref m_twidentitylegacyDs,
                threaddata.dg,
                threaddata.dat,
                threaddata.msg,
                ref threaddata.twuserinterface
            );

            // Update the data block...
            m_twaincommand.Update(m_lIndexDatUserinterface, threaddata);
        }

        public STS DatUserinterface(DG a_dg, MSG a_msg, ref TW_USERINTERFACE a_twuserinterface)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if (this.m_runinuithreaddelegate == null)
            {
                if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
                {
                    lock (m_lockTwain)
                    {
                        // Set our command variables...
                        ThreadData threaddata = default(ThreadData);
                        threaddata.twuserinterface = a_twuserinterface;
                        threaddata.twuserinterface.hParent = m_intptrHwnd;
                        threaddata.dg = a_dg;
                        threaddata.msg = a_msg;
                        threaddata.dat = DAT.USERINTERFACE;
                        m_lIndexDatUserinterface = m_twaincommand.Submit(threaddata);

                        // Submit the command and wait for the reply...
                        CallerToThreadSet();
                        ThreadToCallerWaitOne();

                        // Return the result...
                        a_twuserinterface = m_twaincommand.Get(m_lIndexDatUserinterface).twuserinterface;
                        sts = m_twaincommand.Get(m_lIndexDatUserinterface).sts;

                        // Clear the command variables...
                        m_twaincommand.Delete(m_lIndexDatUserinterface);
                    }
                    return (sts);
                }
            }

            // Well this is weird.  I'm not sure how this design snuck past,
            // I assume it's because of the async nature of the button presses,
            // so it's easier to monitor a boolean.  Regardless, we need to
            // use this data to do the right thing...
            if (m_blIsMsgclosedsok || m_blIsMsgclosedsreq)
            {
                a_msg = MSG.DISABLEDS;
            }

            // If we're doing a DISABLEDS, use the values we remembered from
            // the last ENABLEDS...
            TW_USERINTERFACE twuserinterface = a_twuserinterface;
            if (a_msg == MSG.DISABLEDS)
            {
                twuserinterface = m_twuserinterface;
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.USERINTERFACE.ToString(), a_msg.ToString(), UserinterfaceToCsv(twuserinterface));
            }

            // We need this to handle data sources that return MSG_XFERREADY in
            // the midst of processing MSG_ENABLEDS...
            if (a_msg == MSG.ENABLEDS)
            {
                m_blAcceptXferReady = true;
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (this.m_runinuithreaddelegate == null)
                    {
                        if (m_blUseLegacyDSM)
                        {
                            sts = (STS)WindowsTwain32DsmEntryUserinterface(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.USERINTERFACE, a_msg, ref twuserinterface);
                        }
                        else
                        {
                            sts = (STS)WindowsTwaindsmDsmEntryUserinterface(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.USERINTERFACE, a_msg, ref twuserinterface);
                        }
                    }
                    else
                    {
                        if (m_blUseLegacyDSM)
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twuserinterface = a_twuserinterface;
                                threaddata.twuserinterface.hParent = m_intptrHwnd;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.USERINTERFACE;
                                m_lIndexDatUserinterface = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatUserinterfaceWindowsTwain32);
                                a_twuserinterface = m_twaincommand.Get(m_lIndexDatUserinterface).twuserinterface;
                                sts = m_twaincommand.Get(m_lIndexDatUserinterface).sts;
                                m_twaincommand.Delete(m_lIndexDatUserinterface);
                            }
                        }
                        else
                        {
                            lock (m_lockTwain)
                            {
                                ThreadData threaddata = default(ThreadData);
                                threaddata.twuserinterface = a_twuserinterface;
                                threaddata.twuserinterface.hParent = m_intptrHwnd;
                                threaddata.dg = a_dg;
                                threaddata.msg = a_msg;
                                threaddata.dat = DAT.USERINTERFACE;
                                m_lIndexDatUserinterface = m_twaincommand.Submit(threaddata);
                                RunInUiThread(DatUserinterfaceWindowsTwainDsm);
                                a_twuserinterface = m_twaincommand.Get(m_lIndexDatUserinterface).twuserinterface;
                                sts = m_twaincommand.Get(m_lIndexDatUserinterface).sts;
                                m_twaincommand.Delete(m_lIndexDatUserinterface);
                            }
                        }
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryUserinterface(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.USERINTERFACE, a_msg, ref twuserinterface);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryUserinterface(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.USERINTERFACE, a_msg, ref twuserinterface);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryUserinterface(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.USERINTERFACE, a_msg, ref twuserinterface);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), "");
            }

            // If we opened, go to state 5...
            if ((a_msg == MSG.ENABLEDS) || (a_msg == MSG.ENABLEDSUIONLY))
            {
                if (sts == STS.SUCCESS)
                {
                    m_state = STATE.S5;

                    // Remember the setting...
                    m_twuserinterface = a_twuserinterface;

                    // MSG_XFERREADY showed up while we were still processing MSG_ENABLEDS
                    if ((sts == STS.SUCCESS) && m_blAcceptXferReady && m_blIsMsgxferready)
                    {
                        m_blAcceptXferReady = false;
                        m_state = STATE.S6;
                        // TBD
                        //lock (m_lockTwain)
                        //{
                        //    m_threaddata = default(ThreadData);
                        //}
                        CallerToThreadSet();
                    }
                }
            }

            // If we disabled, go to state 4...
            else if (a_msg == MSG.DISABLEDS)
            {
                if (sts == STS.SUCCESS)
                {
                    m_blIsMsgclosedsreq = false;
                    m_blIsMsgclosedsok = false;
                    m_state = STATE.S4;
                }
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        /// <summary>
        /// Get/Set the Xfer Group...
        /// </summary>
        /// <param name="a_dg">Data group</param>
        /// <param name="a_msg">Operation</param>
        /// <param name="a_twuint32">XFERGROUP structure</param>
        /// <returns>TWAIN status</returns>
        public STS DatXferGroup(DG a_dg, MSG a_msg, ref UInt32 a_twuint32)
        {
            STS sts;

            // Submit the work to the TWAIN thread...
            if ((m_threadTwain != null) && (m_threadTwain.ManagedThreadId != Thread.CurrentThread.ManagedThreadId))
            {
                lock (m_lockTwain)
                {
                    // Set our command variables...
                    ThreadData threaddata = default(ThreadData);
                    threaddata.twuint32 = a_twuint32;
                    threaddata.dg = a_dg;
                    threaddata.msg = a_msg;
                    threaddata.dat = DAT.XFERGROUP;
                    long lIndex = m_twaincommand.Submit(threaddata);

                    // Submit the command and wait for the reply...
                    CallerToThreadSet();
                    ThreadToCallerWaitOne();

                    // Return the result...
                    a_twuint32 = m_twaincommand.Get(lIndex).twuint32;
                    sts = m_twaincommand.Get(lIndex).sts;

                    // Clear the command variables...
                    m_twaincommand.Delete(lIndex);
                }
                return (sts);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendBefore(a_dg.ToString(), DAT.XFERGROUP.ToString(), a_msg.ToString(), XfergroupToCsv(a_twuint32));
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryXfergroup(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.XFERGROUP, a_msg, ref a_twuint32);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryXfergroup(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.XFERGROUP, a_msg, ref a_twuint32);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    if (GetMachineWordBitSize() == 32)
                    {
                        sts = (STS)LinuxDsmEntryXfergroup(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, a_dg, DAT.XFERGROUP, a_msg, ref a_twuint32);
                    }
                    else
                    {
                        sts = (STS)Linux64DsmEntryXfergroup(ref m_twidentityApp, ref m_twidentityDs, a_dg, DAT.XFERGROUP, a_msg, ref a_twuint32);
                    }
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryXfergroup(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, a_dg, DAT.XFERGROUP, a_msg, ref a_twuint32);
                }
                catch
                {
                    // The driver crashed...
                    Log.LogSendAfter(STS.BUMMER.ToString(), "");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                Log.LogSendAfter(STS.BUMMER.ToString(), "");
                return (STS.BUMMER);
            }

            // Log it...
            if (Log.GetLevel() > 0)
            {
                Log.LogSendAfter(sts.ToString(), XfergroupToCsv(a_twuint32));
            }

            // All done...
            return (AutoDatStatus(sts));
        }

        #endregion Public DSM_Entry calls...

        ///////////////////////////////////////////////////////////////////////////////
        // Public Definitions, this is where you get the callback definitions for
        // handling device events and scanning...
        ///////////////////////////////////////////////////////////////////////////////

        #region Public Definitions...

        /// <summary>
        /// The form of the device event callback used by the caller when they are
        /// running in states 4, 5, 6 and 7...
        /// </summary>
        /// <returns></returns>
        public delegate STS DeviceEventCallback();

        /// <summary>
        /// The form of the callback used by the caller when they are running
        /// in states 5, 6 and 7; anything after DG_CONTROL / DAT_USERINTERFACE /
        /// MSG_ENABLEDS* until DG_CONTROL / DAT_USERINTERFACE / MSG_DISABLEDS...
        /// </summary>
        /// <returns></returns>
        public delegate STS ScanCallback(bool a_blClosing);

        /// <summary>
        /// We use this to run code in the context of the caller's UI thread...
        /// </summary>
        /// <param name="a_action">code to run</param>
        public delegate void RunInUiThreadDelegate(Action a_action);

        #endregion Public Definitions...

        ///////////////////////////////////////////////////////////////////////////////
        // Private Functions, the main thread is in here...
        ///////////////////////////////////////////////////////////////////////////////

        #region Private Functions...

        /// <summary>
        /// This is our main loop where we issue commands to the TWAIN
        /// object on behalf of the caller.  This function runs in its
        /// own thread...
        /// </summary>
        private void Main()
        {
            bool blRunning;
            bool blScanning;
            long lIndex;
            ThreadData threaddata;

            // Okay, we're ready to run...
            m_autoreseteventThreadStarted.Set();
            Log.Info("main>>> thread started...");

            //
            // We have three different ways of driving the TWAIN driver...
            //
            // First, we can accept a direct command from the user for commands
            // that move from state 2 to state 4, and for any commands that are
            // issued in state 4.
            //
            // Second, we have a scanning callback function, that operates when
            // we are transferring images; this means that we don't want the
            // user making those calls directly.
            //
            // Third, we have a rollback function, that allows the calls to
            // move anywhere from state 7 to state 2; what this means is that we
            // don't want the user making those calls directly.
            //
            // The trick is to move smoothly between these three styles of
            // access, and what we find is that the first and second are pretty
            // easy to do, but the third one is tricky...
            //
            blRunning = true;
            blScanning = false;
            while (blRunning)
            {
                // Get the next item, if we don't have anything, then we may
                // need to wait...
                if (!m_twaincommand.GetNext(out lIndex, out threaddata))
                {
                    // If we're not scanning, then wait for a command to wake
                    // us up...
                    if (!blScanning)
                    {
                        CallerToThreadWaitOne();
                        m_twaincommand.GetNext(out lIndex, out threaddata);
                    }
                }

                // Process device events...
                if (IsMsgDeviceEvent())
                {
                    m_deviceeventcallback();
                }

                // We don't have a direct command, it's either a rollback request,
                // a request to run the scan callback, or its a false positive,
                // which we can safely ignore...
                if (threaddata.dat == default(DAT))
                {
                    // The caller has asked us to rollback the state machine...
                    if (threaddata.blRollback)
                    {
                        threaddata.blRollback = false;
                        Rollback(threaddata.stateRollback);
                        blScanning = (threaddata.stateRollback >= STATE.S5);
                        blRunning = (threaddata.stateRollback > STATE.S2);
                        if (!blRunning)
                        {
                            m_scancallback(true);
                        }
                        ThreadToRollbackSet();
                    }

                    // Callback stuff here between MSG_ENABLEDS* and MSG_DISABLEDS...
                    else if (GetState() >= STATE.S5)
                    {
                        m_scancallback(false);
                        blScanning = true;
                    }

                    // We're done scanning...
                    else
                    {
                        blScanning = false;
                    }

                    // Tag the command as complete...
                    m_twaincommand.Complete(lIndex, threaddata);

                    // Go back to the top...
                    continue;
                }

                // Otherwise, directly issue the command...
                switch (threaddata.dat)
                {
                    // Unrecognized DAT...
                    default:
                        if (m_state < STATE.S4)
                        {
                            threaddata.sts = DsmEntryNullDest(threaddata.dg, threaddata.dat, threaddata.msg, threaddata.twmemref);
                        }
                        else
                        {
                            threaddata.sts = DsmEntry(threaddata.dg, threaddata.dat, threaddata.msg, threaddata.twmemref);
                        }
                        break;

                    // I have no idea why I'm including this...
                    case DAT.AUDIOINFO:
                        threaddata.sts = DatAudioinfo(threaddata.dg, threaddata.msg, ref threaddata.twaudioinfo);
                        break;

                    // Negotiation commands...
                    case DAT.CAPABILITY:
                        threaddata.sts = DatCapability(threaddata.dg, threaddata.msg, ref threaddata.twcapability);
                        break;

                    // CIE color...
                    case DAT.CIECOLOR:
                        threaddata.sts = DatCiecolor(threaddata.dg, threaddata.msg, ref threaddata.twciecolor);
                        break;

                    // Snapshots...
                    case DAT.CUSTOMDSDATA:
                        threaddata.sts = DatCustomdsdata(threaddata.dg, threaddata.msg, ref threaddata.twcustomdsdata);
                        break;

                    // Functions...
                    case DAT.ENTRYPOINT:
                        threaddata.sts = DatEntrypoint(threaddata.dg, threaddata.msg, ref threaddata.twentrypoint);
                        break;

                    // Image meta data...
                    case DAT.EXTIMAGEINFO:
                        threaddata.sts = DatExtimageinfo(threaddata.dg, threaddata.msg, ref threaddata.twextimageinfo);
                        break;

                    // Filesystem...
                    case DAT.FILESYSTEM:
                        threaddata.sts = DatFilesystem(threaddata.dg, threaddata.msg, ref threaddata.twfilesystem);
                        break;

                    // Filter...
                    case DAT.FILTER:
                        threaddata.sts = DatFilter(threaddata.dg, threaddata.msg, ref threaddata.twfilter);
                        break;

                    // Grayscale...
                    case DAT.GRAYRESPONSE:
                        threaddata.sts = DatGrayresponse(threaddata.dg, threaddata.msg, ref threaddata.twgrayresponse);
                        break;

                    // ICC color profiles...
                    case DAT.ICCPROFILE:
                        threaddata.sts = DatIccprofile(threaddata.dg, threaddata.msg, ref threaddata.twmemory);
                        break;

                    // Enumerate and Open commands...
                    case DAT.IDENTITY:
                        threaddata.sts = DatIdentity(threaddata.dg, threaddata.msg, ref threaddata.twidentity);
                        break;

                    // More meta data...
                    case DAT.IMAGEINFO:
                        threaddata.sts = DatImageinfo(threaddata.dg, threaddata.msg, ref threaddata.twimageinfo);
                        break;

                    // File xfer...
                    case DAT.IMAGEFILEXFER:
                        threaddata.sts = DatImagefilexfer(threaddata.dg, threaddata.msg);
                        break;

                    // Image layout commands...
                    case DAT.IMAGELAYOUT:
                        threaddata.sts = DatImagelayout(threaddata.dg, threaddata.msg, ref threaddata.twimagelayout);
                        break;

                    // Memory file transfer (yes, we're using TW_IMAGEMEMXFER, that's okay)...
                    case DAT.IMAGEMEMFILEXFER:
                        threaddata.sts = DatImagememfilexfer(threaddata.dg, threaddata.msg, ref threaddata.twimagememxfer);
                        break;

                    // Memory transfer...
                    case DAT.IMAGEMEMXFER:
                        threaddata.sts = DatImagememxfer(threaddata.dg, threaddata.msg, ref threaddata.twimagememxfer);
                        break;

                    // Native transfer...
                    case DAT.IMAGENATIVEXFER:
                        threaddata.sts = DatImagenativexfer(threaddata.dg, threaddata.msg, ref threaddata.bitmap);
                        break;

                    // JPEG compression...
                    case DAT.JPEGCOMPRESSION:
                        threaddata.sts = DatJpegcompression(threaddata.dg, threaddata.msg, ref threaddata.twjpegcompression);
                        break;

                    // Palette8...
                    case DAT.PALETTE8:
                        threaddata.sts = DatPalette8(threaddata.dg, threaddata.msg, ref threaddata.twpalette8);
                        break;

                    // DSM commands...
                    case DAT.PARENT:
                        threaddata.sts = DatParent(threaddata.dg, threaddata.msg, ref threaddata.intptrHwnd);
                        break;

                    // Raw commands...
                    case DAT.PASSTHRU:
                        threaddata.sts = DatPassthru(threaddata.dg, threaddata.msg, ref threaddata.twpassthru);
                        break;

                    // Pending transfers...
                    case DAT.PENDINGXFERS:
                        threaddata.sts = DatPendingxfers(threaddata.dg, threaddata.msg, ref threaddata.twpendingxfers);
                        break;

                    // RGB...
                    case DAT.RGBRESPONSE:
                        threaddata.sts = DatRgbresponse(threaddata.dg, threaddata.msg, ref threaddata.twrgbresponse);
                        break;

                    // Setup file transfer...
                    case DAT.SETUPFILEXFER:
                        threaddata.sts = DatSetupfilexfer(threaddata.dg, threaddata.msg, ref threaddata.twsetupfilexfer);
                        break;

                    // Get memory info...
                    case DAT.SETUPMEMXFER:
                        threaddata.sts = DatSetupmemxfer(threaddata.dg, threaddata.msg, ref threaddata.twsetupmemxfer);
                        break;

                    // Status text...
                    case DAT.STATUSUTF8:
                        threaddata.sts = DatStatusutf8(threaddata.dg, threaddata.msg, ref threaddata.twstatusutf8);
                        break;

                    // Scan and GUI commands...
                    case DAT.USERINTERFACE:
                        threaddata.sts = DatUserinterface(threaddata.dg, threaddata.msg, ref threaddata.twuserinterface);
                        if (threaddata.sts == STS.SUCCESS)
                        {
                            if ((threaddata.dg == DG.CONTROL) && (threaddata.dat == DAT.USERINTERFACE) && (threaddata.msg == MSG.DISABLEDS))
                            {
                                blScanning = false;
                            }
                            else if ((threaddata.dg == DG.CONTROL) && (threaddata.dat == DAT.USERINTERFACE) && (threaddata.msg == MSG.DISABLEDS))
                            {
                                if (threaddata.twuserinterface.ShowUI == 0)
                                {
                                    blScanning = true;
                                }
                            }
                        }
                        break;

                    // Transfer group...
                    case DAT.XFERGROUP:
                        threaddata.sts = DatXferGroup(threaddata.dg, threaddata.msg, ref threaddata.twuint32);
                        break;
                }

                // Report to the caller that we're done, and loop back up for another...
                m_twaincommand.Complete(lIndex, threaddata);
                ThreadToCallerSet();
            }

            // Some insurance to make sure we loosen up the caller...
            m_scancallback(true);
            ThreadToCallerSet();
        }

        /// <summary>
        /// Use an event message to set the appropriate flags...
        /// </summary>
        /// <param name="a_msg">Message to process</param>
        private void ProcessEvent(MSG a_msg)
        {
            switch (a_msg)
            {
                // Do nothing...
                default:
                    break;

                // If we're in state 5, then go to state 6...
                case MSG.XFERREADY:
                    if (m_blAcceptXferReady)
                    {
                        // MSG_XFERREADY arrived during the handling of the
                        // call to MSG_ENABLEDS.  We have to defer processing
                        // it as late as possible...
                        if (m_state == STATE.S4)
                        {
                            m_blIsMsgxferready = true;
                        }

                        // MSG_XFERREADY arrived after the completion of the
                        // call to MSG_ENABLEDS.  We can just do it...
                        else
                        {
                            m_blAcceptXferReady = false;
                            m_state = STATE.S6;
                            m_blIsMsgxferready = true;
                            // TBD
                            //lock (m_lockTwain)
                            //{
                            //    m_threaddata = default(ThreadData);
                            //}
                            CallerToThreadSet();
                        }
                    }
                    break;

                // The cancel button was pressed...
                case MSG.CLOSEDSREQ:
                    m_blIsMsgclosedsreq = true;
                    CallerToThreadSet();
                    break;

                // The OK button was pressed...
                case MSG.CLOSEDSOK:
                    m_blIsMsgclosedsok = true;
                    CallerToThreadSet();
                    break;

                // A device event arrived...
                case MSG.DEVICEEVENT:
                    m_blIsMsgdeviceevent = true;
                    CallerToThreadSet();
                    break;
            }
        }

        /// <summary>
        /// TWAIN needs help, if we want it to run stuff in our main
        /// UI thread...
        /// </summary>
        /// <param name="code">the code to run</param>
        private void RunInUiThread(Action a_action)
        {
            m_runinuithreaddelegate(a_action);
        }

        /// <summary>
        /// The caller is asking the thread to wake-up...
        /// </summary>
        private void CallerToThreadSet()
        {
            m_autoreseteventCaller.Set();
        }

        /// <summary>
        /// The thread is waiting for the caller to wake it...
        /// </summary>
        private bool CallerToThreadWaitOne()
        {
            return (m_autoreseteventCaller.WaitOne());
        }

        /// <summary>
        /// The common start to every capability csv...
        /// </summary>
        /// <param name="a_cap">Capability number</param>
        /// <param name="a_twon">Container</param>
        /// <param name="a_twty">Data type</param>
        /// <returns></returns>
        private CSV Common(CAP a_cap, TWON a_twon, TWTY a_twty)
        {
            CSV csv = new CSV();

            // Add the capability...
            string szCap = a_cap.ToString();
            if (!szCap.Contains("_"))
            {
                szCap = "0x" + ((ushort)a_cap).ToString("X");
            }

            // Build the CSV...
            csv.Add(szCap);
            csv.Add("TWON_" + a_twon);
            csv.Add("TWTY_" + a_twty);

            // And return it...
            return (csv);
        }

        /// <summary>
        /// Has a device event arrived?  Make sure to clear it, because
        /// we can get many of these.  We don't have to worry about a
        /// race condition, because the caller is expected to drain the
        /// driver of all events.
        /// </summary>
        /// <returns>True if a device event is pending</returns>
        private bool IsMsgDeviceEvent()
        {
            if (m_blIsMsgdeviceevent)
            {
                m_blIsMsgdeviceevent = false;
                return (true);
            }
            return (false);
        }

        /// <summary>
        /// The thread is asking the caller to wake-up...
        /// </summary>
        private void ThreadToCallerSet()
        {
            m_autoreseteventThread.Set();
        }

        /// <summary>
        /// The caller is waiting for the thread to wake it...
        /// </summary>
        /// <returns>Result of the wait</returns>
        private bool ThreadToCallerWaitOne()
        {
            return (m_autoreseteventThread.WaitOne());
        }

        /// <summary>
        /// The thread is asking the rollback to wake-up...
        /// </summary>
        private void ThreadToRollbackSet()
        {
            m_autoreseteventRollback.Set();
        }

        /// <summary>
        /// The rollback is waiting for the thread to wake it...
        /// </summary>
        /// <returns>Result of the wait</returns>
        private bool ThreadToRollbackWaitOne()
        {
            return (m_autoreseteventRollback.WaitOne());
        }

        /// <summary>
        /// Automatically collect the condition code for TWRC_FAILURE's...
        /// </summary>
        /// <param name="a_sts">The return code from the last operation</param>
        /// <returns>The final statue return</returns>
        private STS AutoDatStatus(STS a_sts)
        {
            STS sts;
            TW_STATUS twstatus = new TW_STATUS();

            // Automatic system is off, or the status is not TWRC_FAILURE, so just return the status we got...
            if (!m_blAutoDatStatus || (a_sts != STS.FAILURE))
            {
                return (a_sts);
            }

            // Windows...
            if (ms_platform == Platform.WINDOWS)
            {
                // Issue the command...
                try
                {
                    if (m_blUseLegacyDSM)
                    {
                        sts = (STS)WindowsTwain32DsmEntryStatus(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.STATUS, MSG.GET, ref twstatus);
                    }
                    else
                    {
                        sts = (STS)WindowsTwaindsmDsmEntryStatus(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.STATUS, MSG.GET, ref twstatus);
                    }
                }
                catch
                {
                    // The driver crashed...
                    TWAINWorkingGroup.Log.Error("Driver crash...");
                    return (STS.BUMMER);
                }
            }

            // Linux...
            else if (ms_platform == Platform.LINUX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)LinuxDsmEntryStatus(ref m_twidentitylegacyApp, ref m_twidentitylegacyDs, DG.CONTROL, DAT.STATUS, MSG.GET, ref twstatus);
                }
                catch
                {
                    // The driver crashed...
                    TWAINWorkingGroup.Log.Error("Driver crash...");
                    return (STS.BUMMER);
                }
            }

            // Mac OS X, which has to be different...
            else if (ms_platform == Platform.MACOSX)
            {
                // Issue the command...
                try
                {
                    sts = (STS)MacosxDsmEntryStatus(ref m_twidentitymacosxApp, ref m_twidentitymacosxDs, DG.CONTROL, DAT.STATUS, MSG.GET, ref twstatus);
                }
                catch
                {
                    // The driver crashed...
                    TWAINWorkingGroup.Log.Error("Driver crash...");
                    return (STS.BUMMER);
                }
            }

            // Uh-oh...
            else
            {
                TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
                return (STS.BUMMER);
            }

            // Uh-oh, the status call failed...
            if (sts != STS.SUCCESS)
            {
                return (a_sts);
            }

            // All done...
            return ((STS)(STSCC + twstatus.ConditionCode));
        }

        /// <summary>
        /// 32-bit or 64-bit...
        /// </summary>
        /// <returns>Number of bits in the machine word for this process</returns>
        public static int GetMachineWordBitSize()
        {
            return (Marshal.SizeOf(typeof(IntPtr)) * 8);
        }

        /// <summary>
        /// Quick access to our platform id...
        /// </summary>
        /// <returns></returns>
        public static Platform GetPlatform()
        {
            // First pass...
            if (ms_blFirstPassGetPlatform)
            {
                // Dont'c come in here again...
                ms_blFirstPassGetPlatform = false;

                // We're Windows...
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    ms_platform = Platform.WINDOWS;
                }

                // We're Mac OS X (this has to come before LINUX!!!)...
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    ms_platform = Platform.MACOSX;
                }

                // We're Linux...
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    ms_platform = Platform.LINUX;
                }
                // We have a problem, Log will throw for us...
                else
                {
                    ms_platform = Platform.UNKNOWN;
                    TWAINWorkingGroup.Log.Assert("Unsupported platform..." + ms_platform);
                }
            }

            return (ms_platform);
        }

        /// <summary>
        /// Convert the contents of a capability to a string that we can show in
        /// our simple GUI...
        /// </summary>
        /// <param name="a_twty">Data type</param>
        /// <param name="a_intptr">Pointer to the data</param>
        /// <param name="a_iIndex">Index of the item in the data</param>
        /// <returns>Data in CSV form</returns>
        public string GetIndexedItem(TWTY a_twty, IntPtr a_intptr, int a_iIndex)
        {
            IntPtr intptr;

            // Index by type...
            switch (a_twty)
            {
                default:
                    return ("Get Capability: (unrecognized item type)..." + a_twty);

                case TWTY.INT8:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(1 * a_iIndex));
                        sbyte i8Value = (sbyte)Marshal.PtrToStructure(intptr, typeof(sbyte));
                        return (i8Value.ToString());
                    }

                case TWTY.INT16:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(2 * a_iIndex));
                        short i16Value = (short)Marshal.PtrToStructure(intptr, typeof(short));
                        return (i16Value.ToString());
                    }

                case TWTY.INT32:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        int i32Value = (int)Marshal.PtrToStructure(intptr, typeof(int));
                        return (i32Value.ToString());
                    }

                case TWTY.UINT8:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(1 * a_iIndex));
                        byte u8Value = (byte)Marshal.PtrToStructure(intptr, typeof(byte));
                        return (u8Value.ToString());
                    }

                case TWTY.BOOL:
                case TWTY.UINT16:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(2 * a_iIndex));
                        ushort u16Value = (ushort)Marshal.PtrToStructure(intptr, typeof(ushort));
                        return (u16Value.ToString());
                    }

                case TWTY.UINT32:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        uint u32Value = (uint)Marshal.PtrToStructure(intptr, typeof(uint));
                        return (u32Value.ToString());
                    }

                case TWTY.FIX32:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        TW_FIX32 twfix32 = (TW_FIX32)Marshal.PtrToStructure(intptr, typeof(TW_FIX32));
                        return (((double)twfix32.Whole + ((double)twfix32.Frac / 65536.0)).ToString());
                    }

                case TWTY.FRAME:
                    {
                        CSV csv = new CSV();
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(16 * a_iIndex));
                        TW_FRAME twframe = (TW_FRAME)Marshal.PtrToStructure(intptr, typeof(TW_FRAME));
                        csv.Add(((double)twframe.Left.Whole + ((double)twframe.Left.Frac / 65536.0)).ToString());
                        csv.Add(((double)twframe.Top.Whole + ((double)twframe.Top.Frac / 65536.0)).ToString());
                        csv.Add(((double)twframe.Right.Whole + ((double)twframe.Right.Frac / 65536.0)).ToString());
                        csv.Add(((double)twframe.Bottom.Whole + ((double)twframe.Bottom.Frac / 65536.0)).ToString());
                        return (csv.Get());
                    }

                case TWTY.STR32:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(34 * a_iIndex));
                        TW_STR32 twstr32 = (TW_STR32)Marshal.PtrToStructure(intptr, typeof(TW_STR32));
                        return (twstr32.Get());
                    }

                case TWTY.STR64:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(66 * a_iIndex));
                        TW_STR64 twstr64 = (TW_STR64)Marshal.PtrToStructure(intptr, typeof(TW_STR64));
                        return (twstr64.Get());
                    }

                case TWTY.STR128:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(130 * a_iIndex));
                        TW_STR128 twstr128 = (TW_STR128)Marshal.PtrToStructure(intptr, typeof(TW_STR128));
                        return (twstr128.Get());
                    }

                case TWTY.STR255:
                    {
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(256 * a_iIndex));
                        TW_STR255 twstr255 = (TW_STR255)Marshal.PtrToStructure(intptr, typeof(TW_STR255));
                        return (twstr255.Get());
                    }
            }
        }

        /// <summary>
        /// Convert the value of a string into a capability...
        /// </summary>
        /// <param name="a_twon">Container type</param>
        /// <param name="a_twty">Data type</param>
        /// <param name="a_intptr">Point to the data</param>
        /// <param name="a_iIndex">Index for item in the data</param>
        /// <param name="a_szValue">CSV value to be used to set the data</param>
        /// <returns>Empty string or an error string</returns>
        public string SetIndexedItem(TWON a_twon, TWTY a_twty, IntPtr a_intptr, int a_iIndex, string a_szValue)
        {
            IntPtr intptr;

            // Index by type...
            switch (a_twty)
            {
                default:
                    return ("Set Capability: (unrecognized item type)..." + a_twty);

                case TWTY.INT8:
                    {
                        // We do this to make sure the entire Item value is overwritten...
                        if (a_twon == TWON.ONEVALUE)
                        {
                            int i32Value = sbyte.Parse(a_szValue);
                            Marshal.StructureToPtr(i32Value, a_intptr, true);
                            return ("");
                        }
                        // These items have to be packed on the type sizes...
                        else
                        {
                            sbyte i8Value = sbyte.Parse(a_szValue);
                            intptr = (IntPtr)((ulong)a_intptr + (ulong)(1 * a_iIndex));
                            Marshal.StructureToPtr(i8Value, intptr, true);
                            return ("");
                        }
                    }

                case TWTY.INT16:
                    {
                        // We do this to make sure the entire Item value is overwritten...
                        if (a_twon == TWON.ONEVALUE)
                        {
                            int i32Value = short.Parse(a_szValue);
                            Marshal.StructureToPtr(i32Value, a_intptr, true);
                            return ("");
                        }
                        // These items have to be packed on the type sizes...
                        else
                        {
                            short i16Value = short.Parse(a_szValue);
                            intptr = (IntPtr)((ulong)a_intptr + (ulong)(2 * a_iIndex));
                            Marshal.StructureToPtr(i16Value, intptr, true);
                            return ("");
                        }
                    }

                case TWTY.INT32:
                    {
                        int i32Value = int.Parse(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        Marshal.StructureToPtr(i32Value, intptr, true);
                        return ("");
                    }

                case TWTY.UINT8:
                    {
                        // We do this to make sure the entire Item value is overwritten...
                        if (a_twon == TWON.ONEVALUE)
                        {
                            uint u32Value = byte.Parse(a_szValue);
                            Marshal.StructureToPtr(u32Value, a_intptr, true);
                            return ("");
                        }
                        // These items have to be packed on the type sizes...
                        else
                        {
                            byte u8Value = byte.Parse(a_szValue);
                            intptr = (IntPtr)((ulong)a_intptr + (ulong)(1 * a_iIndex));
                            Marshal.StructureToPtr(u8Value, intptr, true);
                            return ("");
                        }
                    }

                case TWTY.BOOL:
                case TWTY.UINT16:
                    {
                        // We do this to make sure the entire Item value is overwritten...
                        if (a_twon == TWON.ONEVALUE)
                        {
                            uint u32Value = ushort.Parse(a_szValue);
                            Marshal.StructureToPtr(u32Value, a_intptr, true);
                            return ("");
                        }
                        else
                        {
                            ushort u16Value = ushort.Parse(a_szValue);
                            intptr = (IntPtr)((ulong)a_intptr + (ulong)(2 * a_iIndex));
                            Marshal.StructureToPtr(u16Value, intptr, true);
                            return ("");
                        }
                    }

                case TWTY.UINT32:
                    {
                        uint u32Value = uint.Parse(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        Marshal.StructureToPtr(u32Value, intptr, true);
                        return ("");
                    }

                case TWTY.FIX32:
                    {
                        TW_FIX32 twfix32 = default(TW_FIX32);
                        twfix32.Whole = (short)Convert.ToDouble(a_szValue);
                        twfix32.Frac = (ushort)((Convert.ToDouble(a_szValue) - (double)twfix32.Whole) * 65536.0);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(4 * a_iIndex));
                        Marshal.StructureToPtr(twfix32, intptr, true);
                        return ("");
                    }

                case TWTY.FRAME:
                    {
                        TW_FRAME twframe = default(TW_FRAME);
                        string[] asz = CSV.Parse(a_szValue);
                        twframe.Left.Whole = (short)Convert.ToDouble(asz[0]);
                        twframe.Left.Frac = (ushort)((Convert.ToDouble(asz[0]) - (double)twframe.Left.Whole) * 65536.0);
                        twframe.Top.Whole = (short)Convert.ToDouble(asz[1]);
                        twframe.Top.Frac = (ushort)((Convert.ToDouble(asz[1]) - (double)twframe.Top.Whole) * 65536.0);
                        twframe.Right.Whole = (short)Convert.ToDouble(asz[2]);
                        twframe.Right.Frac = (ushort)((Convert.ToDouble(asz[2]) - (double)twframe.Right.Whole) * 65536.0);
                        twframe.Bottom.Whole = (short)Convert.ToDouble(asz[3]);
                        twframe.Bottom.Frac = (ushort)((Convert.ToDouble(asz[3]) - (double)twframe.Bottom.Whole) * 65536.0);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(16 * a_iIndex));
                        Marshal.StructureToPtr(twframe, intptr, true);
                        return ("");
                    }

                case TWTY.STR32:
                    {
                        TW_STR32 twstr32 = default(TW_STR32);
                        twstr32.Set(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(34 * a_iIndex));
                        Marshal.StructureToPtr(twstr32, intptr, true);
                        return ("");
                    }

                case TWTY.STR64:
                    {
                        TW_STR64 twstr64 = default(TW_STR64);
                        twstr64.Set(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(66 * a_iIndex));
                        Marshal.StructureToPtr(twstr64, intptr, true);
                        return ("");
                    }

                case TWTY.STR128:
                    {
                        TW_STR128 twstr128 = default(TW_STR128);
                        twstr128.Set(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(130 * a_iIndex));
                        Marshal.StructureToPtr(twstr128, intptr, true);
                        return ("");
                    }

                case TWTY.STR255:
                    {
                        TW_STR255 twstr255 = default(TW_STR255);
                        twstr255.Set(a_szValue);
                        intptr = (IntPtr)((ulong)a_intptr + (ulong)(256 * a_iIndex));
                        Marshal.StructureToPtr(twstr255, intptr, true);
                        return ("");
                    }
            }
        }

        /// <summary>
        /// Convert strings into a range...
        /// </summary>
        /// <param name="a_twty">Data type</param>
        /// <param name="a_intptr">Pointer to the data</param>
        /// <param name="a_asz">List of strings</param>
        /// <returns>Empty string or an error string</returns>
        public string SetRangeItem(TWTY a_twty, IntPtr a_intptr, string[] a_asz)
        {
            TW_RANGE twrange = default(TW_RANGE);
            TW_RANGE_MACOSX twrangemacosx = default(TW_RANGE_MACOSX);
            TW_RANGE_FIX32 twrangefix32 = default(TW_RANGE_FIX32);
            TW_RANGE_FIX32_MACOSX twrangefix32macosx = default(TW_RANGE_FIX32_MACOSX);

            // Index by type...
            switch (a_twty)
            {
                default:
                    return ("Set Capability: (unrecognized item type)..." + a_twty);

                case TWTY.INT8:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = (uint)sbyte.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = (uint)sbyte.Parse(a_asz[4]);
                            twrangemacosx.StepSize = (uint)sbyte.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = (uint)sbyte.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = (uint)sbyte.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = (uint)sbyte.Parse(a_asz[3]);
                            twrange.MaxValue = (uint)sbyte.Parse(a_asz[4]);
                            twrange.StepSize = (uint)sbyte.Parse(a_asz[5]);
                            twrange.DefaultValue = (uint)sbyte.Parse(a_asz[6]);
                            twrange.CurrentValue = (uint)sbyte.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.INT16:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = (uint)short.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = (uint)short.Parse(a_asz[4]);
                            twrangemacosx.StepSize = (uint)short.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = (uint)short.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = (uint)short.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = (uint)short.Parse(a_asz[3]);
                            twrange.MaxValue = (uint)short.Parse(a_asz[4]);
                            twrange.StepSize = (uint)short.Parse(a_asz[5]);
                            twrange.DefaultValue = (uint)short.Parse(a_asz[6]);
                            twrange.CurrentValue = (uint)short.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.INT32:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = (uint)int.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = (uint)int.Parse(a_asz[4]);
                            twrangemacosx.StepSize = (uint)int.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = (uint)int.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = (uint)int.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = (uint)int.Parse(a_asz[3]);
                            twrange.MaxValue = (uint)int.Parse(a_asz[4]);
                            twrange.StepSize = (uint)int.Parse(a_asz[5]);
                            twrange.DefaultValue = (uint)int.Parse(a_asz[6]);
                            twrange.CurrentValue = (uint)int.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.UINT8:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = (uint)byte.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = (uint)byte.Parse(a_asz[4]);
                            twrangemacosx.StepSize = (uint)byte.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = (uint)byte.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = (uint)byte.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = (uint)byte.Parse(a_asz[3]);
                            twrange.MaxValue = (uint)byte.Parse(a_asz[4]);
                            twrange.StepSize = (uint)byte.Parse(a_asz[5]);
                            twrange.DefaultValue = (uint)byte.Parse(a_asz[6]);
                            twrange.CurrentValue = (uint)byte.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.BOOL:
                case TWTY.UINT16:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = (uint)ushort.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = (uint)ushort.Parse(a_asz[4]);
                            twrangemacosx.StepSize = (uint)ushort.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = (uint)ushort.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = (uint)ushort.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = (uint)ushort.Parse(a_asz[3]);
                            twrange.MaxValue = (uint)ushort.Parse(a_asz[4]);
                            twrange.StepSize = (uint)ushort.Parse(a_asz[5]);
                            twrange.DefaultValue = (uint)ushort.Parse(a_asz[6]);
                            twrange.CurrentValue = (uint)ushort.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.UINT32:
                    {
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangemacosx.ItemType = (uint)a_twty;
                            twrangemacosx.MinValue = uint.Parse(a_asz[3]);
                            twrangemacosx.MaxValue = uint.Parse(a_asz[4]);
                            twrangemacosx.StepSize = uint.Parse(a_asz[5]);
                            twrangemacosx.DefaultValue = uint.Parse(a_asz[6]);
                            twrangemacosx.CurrentValue = uint.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrangemacosx, a_intptr, true);
                        }
                        else
                        {
                            twrange.ItemType = a_twty;
                            twrange.MinValue = uint.Parse(a_asz[3]);
                            twrange.MaxValue = uint.Parse(a_asz[4]);
                            twrange.StepSize = uint.Parse(a_asz[5]);
                            twrange.DefaultValue = uint.Parse(a_asz[6]);
                            twrange.CurrentValue = uint.Parse(a_asz[7]);
                            Marshal.StructureToPtr(twrange, a_intptr, true);
                        }
                        return ("");
                    }

                case TWTY.FIX32:
                    {
                        double dMinValue = Convert.ToDouble(a_asz[3]);
                        double dMaxValue = Convert.ToDouble(a_asz[4]);
                        double dStepSize = Convert.ToDouble(a_asz[5]);
                        double dDefaultValue = Convert.ToDouble(a_asz[6]);
                        double dCurrentValue = Convert.ToDouble(a_asz[7]);
                        if (ms_platform == Platform.MACOSX)
                        {
                            twrangefix32macosx.ItemType = (uint)a_twty;
                            twrangefix32macosx.MinValue.Whole = (short)dMinValue;
                            twrangefix32macosx.MinValue.Frac = (ushort)((dMinValue - (double)twrangefix32macosx.MinValue.Whole) * 65536.0);
                            twrangefix32macosx.MaxValue.Whole = (short)dMaxValue;
                            twrangefix32macosx.MaxValue.Frac = (ushort)((dMaxValue - (double)twrangefix32macosx.MaxValue.Whole) * 65536.0);
                            twrangefix32macosx.StepSize.Whole = (short)dStepSize;
                            twrangefix32macosx.StepSize.Frac = (ushort)((dStepSize - (double)twrangefix32macosx.StepSize.Whole) * 65536.0);
                            twrangefix32macosx.DefaultValue.Whole = (short)dDefaultValue;
                            twrangefix32macosx.DefaultValue.Frac = (ushort)((dDefaultValue - (double)twrangefix32macosx.DefaultValue.Whole) * 65536.0);
                            twrangefix32macosx.CurrentValue.Whole = (short)dCurrentValue;
                            twrangefix32macosx.CurrentValue.Frac = (ushort)((dCurrentValue - (double)twrangefix32macosx.CurrentValue.Whole) * 65536.0);
                            Marshal.StructureToPtr(twrangefix32macosx, a_intptr, true);
                        }
                        else
                        {
                            twrangefix32.ItemType = a_twty;
                            twrangefix32.MinValue.Whole = (short)dMinValue;
                            twrangefix32.MinValue.Frac = (ushort)((dMinValue - (double)twrangefix32.MinValue.Whole) * 65536.0);
                            twrangefix32.MaxValue.Whole = (short)dMaxValue;
                            twrangefix32.MaxValue.Frac = (ushort)((dMaxValue - (double)twrangefix32.MaxValue.Whole) * 65536.0);
                            twrangefix32.StepSize.Whole = (short)dStepSize;
                            twrangefix32.StepSize.Frac = (ushort)((dStepSize - (double)twrangefix32.StepSize.Whole) * 65536.0);
                            twrangefix32.DefaultValue.Whole = (short)dDefaultValue;
                            twrangefix32.DefaultValue.Frac = (ushort)((dDefaultValue - (double)twrangefix32.DefaultValue.Whole) * 65536.0);
                            twrangefix32.CurrentValue.Whole = (short)dCurrentValue;
                            twrangefix32.CurrentValue.Frac = (ushort)((dCurrentValue - (double)twrangefix32.CurrentValue.Whole) * 65536.0);
                            Marshal.StructureToPtr(twrangefix32, a_intptr, true);
                        }
                        return ("");
                    }
            }
        }

        /// <summary>
        /// Our callback delegate for Windows...
        /// </summary>
        /// <param name="origin">Origin of message</param>
        /// <param name="dest">Message target</param>
        /// <param name="dg">Data group</param>
        /// <param name="dat">Data argument type</param>
        /// <param name="msg">Operation</param>
        /// <param name="twnull">NULL pointer</param>
        /// <returns>TWAIN status</returns>
        private UInt16 WindowsDsmEntryCallbackProxy
        (
            ref TW_IDENTITY_LEGACY origin,
            ref TW_IDENTITY_LEGACY dest,
            DG dg,
            DAT dat,
            MSG msg,
            IntPtr twnull
        )
        {
            ProcessEvent(msg);
            return ((UInt16)STS.SUCCESS);
        }

        /// <summary>
        /// Our callback delegate for Linux...
        /// </summary>
        /// <param name="origin">Origin of message</param>
        /// <param name="dest">Message target</param>
        /// <param name="dg">Data group</param>
        /// <param name="dat">Data argument type</param>
        /// <param name="msg">Operation</param>
        /// <param name="twnull">NULL pointer</param>
        /// <returns>TWAIN status</returns>
        private UInt16 LinuxDsmEntryCallbackProxy
        (
            ref TW_IDENTITY_LEGACY origin,
            ref TW_IDENTITY_LEGACY dest,
            DG dg,
            DAT dat,
            MSG msg,
            IntPtr twnull
        )
        {
            ProcessEvent(msg);
            return ((UInt16)STS.SUCCESS);
        }

        /// <summary>
        /// Our callback delegate for Mac OS X...
        /// </summary>
        /// <param name="origin">Origin of message</param>
        /// <param name="dest">Message target</param>
        /// <param name="dg">Data group</param>
        /// <param name="dat">Data argument type</param>
        /// <param name="msg">Operation</param>
        /// <param name="twnull">NULL pointer</param>
        /// <returns>TWAIN status</returns>
        private UInt16 MacosxDsmEntryCallbackProxy
        (
            ref TW_IDENTITY_MACOSX origin,
            ref TW_IDENTITY_MACOSX dest,
            DG dg,
            DAT dat,
            MSG msg,
            IntPtr twnull
        )
        {
            ProcessEvent(msg);
            return ((UInt16)STS.SUCCESS);
        }

        /// <summary>
        /// Get .NET 'Bitmap' object from memory DIB via stream constructor.
        /// This should work for most DIBs.
        /// </summary>
        /// <param name="a_platform">Our operating system</param>
        /// <param name="a_intptrNative">The pointer to something (presumably a BITMAP or a TIFF image)</param>
        /// <returns>C# Bitmap of image</returns>
        private Image NativeToBitmap(Platform a_platform, IntPtr a_intptrNative)
        {
            ushort u16Magic;
            IntPtr intptrNative;

            // We need the first two bytes to decide if we have a DIB or a TIFF.  Don't
            // forget to lock the silly thing...
            intptrNative = DsmMemLock(a_intptrNative);
            u16Magic = (ushort)Marshal.PtrToStructure(intptrNative, typeof(ushort));

            // Windows uses a DIB, the first usigned short is 40...
            if (u16Magic == 40)
            {
                byte[] bBitmap;
                BITMAPFILEHEADER bitmapfileheader;
                BITMAPINFOHEADER bitmapinfoheader;

                // Our incoming DIB is a bitmap info header...
                bitmapinfoheader = (BITMAPINFOHEADER)Marshal.PtrToStructure(intptrNative, typeof(BITMAPINFOHEADER));

                // Build our file header...
                bitmapfileheader = new BITMAPFILEHEADER();
                bitmapfileheader.bfType = 0x4D42; // "BM"
                bitmapfileheader.bfSize
                    = (uint)Marshal.SizeOf(typeof(BITMAPFILEHEADER)) +
                       bitmapinfoheader.biSize +
                       (bitmapinfoheader.biClrUsed * 4) +
                       bitmapinfoheader.biSizeImage;
                bitmapfileheader.bfOffBits
                    = (uint)Marshal.SizeOf(typeof(BITMAPFILEHEADER)) +
                       bitmapinfoheader.biSize +
                       (bitmapinfoheader.biClrUsed * 4);

                // Copy the file header into our byte array...
                IntPtr intptr = Marshal.AllocHGlobal(Marshal.SizeOf(bitmapfileheader));
                Marshal.StructureToPtr(bitmapfileheader, intptr, true);
                bBitmap = new byte[bitmapfileheader.bfSize];
                Marshal.Copy(intptr, bBitmap, 0, Marshal.SizeOf(bitmapfileheader));
                Marshal.FreeHGlobal(intptr);
                intptr = IntPtr.Zero;

                // Copy the rest of the DIB into our byte array......
                Marshal.Copy(intptrNative, bBitmap, Marshal.SizeOf(typeof(BITMAPFILEHEADER)), (int)bitmapfileheader.bfSize - Marshal.SizeOf(typeof(BITMAPFILEHEADER)));

                // Now we can turn the in-memory bitmap file into a Bitmap object...
                MemoryStream memorystream = new MemoryStream(bBitmap);

                // Unfortunately the stream has to be kept with the bitmap...
                Image bitmapStream = new Image(memorystream);

                // So we make a copy (ick)...
                Image bitmap = new Image(bitmapStream);

                // Cleanup...
                //bitmapStream.Dispose();
                //memorystream.Close();
                bitmapStream = null;
                memorystream = null;
                bBitmap = null;

                // Return our bitmap...
                DsmMemUnlock(a_intptrNative);
                return (bitmap);
            }

            // Linux and Mac OS X use TIFF.  We'll handle a simple Intel TIFF ("II")...
            else if (u16Magic == 0x4949)
            {
                int iTiffSize;
                ulong u64;
                ulong u64Pointer;
                ulong u64TiffHeaderSize;
                ulong u64TiffTagSize;
                byte[] abTiff;
                TIFFHEADER tiffheader;
                TIFFTAG tifftag;

                // Init stuff...
                tiffheader = new TIFFHEADER();
                tifftag = new TIFFTAG();
                u64TiffHeaderSize = (ulong)Marshal.SizeOf(tiffheader);
                u64TiffTagSize = (ulong)Marshal.SizeOf(tifftag);

                // Find the size of the image so we can turn it into a memory stream...
                iTiffSize = 0;
                tiffheader = (TIFFHEADER)Marshal.PtrToStructure(intptrNative, typeof(TIFFHEADER));
                for (u64 = 0; u64 < 999; u64++)
                {
                    u64Pointer = (ulong)intptrNative + u64TiffHeaderSize + (u64TiffTagSize * u64);
                    tifftag = (TIFFTAG)Marshal.PtrToStructure((IntPtr)u64Pointer, typeof(TIFFTAG));

                    // StripOffsets...
                    if (tifftag.u16Tag == 273)
                    {
                        iTiffSize += (int)tifftag.u32Value;
                    }

                    // StripByteCounts...
                    if (tifftag.u16Tag == 279)
                    {
                        iTiffSize += (int)tifftag.u32Value;
                    }
                }

                // No joy...
                if (iTiffSize == 0)
                {
                    DsmMemUnlock(a_intptrNative);
                    return (null);
                }

                // Copy the data to our byte array...
                abTiff = new byte[iTiffSize];
                Marshal.Copy(intptrNative, abTiff, 0, iTiffSize);

                // Move the image into a memory stream...
                MemoryStream memorystream = new MemoryStream(abTiff);

                // Turn the memory stream into an in-memory TIFF image...
                Image imageTiff = new Image(memorystream);

                // Convert the in-memory tiff to a Bitmap object...
                Image bitmap = new Image(imageTiff);

                // Cleanup...
                abTiff = null;
                memorystream = null;
                imageTiff = null;

                // Return our bitmap...
                DsmMemUnlock(a_intptrNative);
                return (bitmap);
            }

            // Uh-oh...
            DsmMemUnlock(a_intptrNative);
            return (null);
        }

        /// <summary>
        /// Convert a public identity to a legacy identity...
        /// </summary>
        /// <param name="a_twidentity">Identity to convert</param>
        /// <returns>Legacy form of identity</returns>
        private TW_IDENTITY_LEGACY TwidentityToTwidentitylegacy(TW_IDENTITY a_twidentity)
        {
            TW_IDENTITY_LEGACY twidentitylegacy = new TW_IDENTITY_LEGACY();
            twidentitylegacy.Id = (uint)a_twidentity.Id;
            twidentitylegacy.Manufacturer = a_twidentity.Manufacturer;
            twidentitylegacy.ProductFamily = a_twidentity.ProductFamily;
            twidentitylegacy.ProductName = a_twidentity.ProductName;
            twidentitylegacy.ProtocolMajor = a_twidentity.ProtocolMajor;
            twidentitylegacy.ProtocolMinor = a_twidentity.ProtocolMinor;
            twidentitylegacy.SupportedGroups = a_twidentity.SupportedGroups;
            twidentitylegacy.Version.Country = a_twidentity.Version.Country;
            twidentitylegacy.Version.Info = a_twidentity.Version.Info;
            twidentitylegacy.Version.Language = a_twidentity.Version.Language;
            twidentitylegacy.Version.MajorNum = a_twidentity.Version.MajorNum;
            twidentitylegacy.Version.MinorNum = a_twidentity.Version.MinorNum;
            return (twidentitylegacy);
        }

        /// <summary>
        /// Convert a public identity to a macosx identity...
        /// </summary>
        /// <param name="a_twidentity">Identity to convert</param>
        /// <returns>Mac OS X form of identity</returns>
        public static TW_IDENTITY_MACOSX TwidentityToTwidentitymacosx(TW_IDENTITY a_twidentity)
        {
            TW_IDENTITY_MACOSX twidentitymacosx = new TW_IDENTITY_MACOSX();
            twidentitymacosx.Id = (uint)a_twidentity.Id;
            twidentitymacosx.Manufacturer = a_twidentity.Manufacturer;
            twidentitymacosx.ProductFamily = a_twidentity.ProductFamily;
            twidentitymacosx.ProductName = a_twidentity.ProductName;
            twidentitymacosx.ProtocolMajor = a_twidentity.ProtocolMajor;
            twidentitymacosx.ProtocolMinor = a_twidentity.ProtocolMinor;
            twidentitymacosx.SupportedGroups = a_twidentity.SupportedGroups;
            twidentitymacosx.Version.Country = a_twidentity.Version.Country;
            twidentitymacosx.Version.Info = a_twidentity.Version.Info;
            twidentitymacosx.Version.Language = a_twidentity.Version.Language;
            twidentitymacosx.Version.MajorNum = a_twidentity.Version.MajorNum;
            twidentitymacosx.Version.MinorNum = a_twidentity.Version.MinorNum;
            return (twidentitymacosx);
        }

        /// <summary>
        /// Convert a legacy identity to a public identity...
        /// </summary>
        /// <param name="a_twidentitylegacy">Legacy identity to convert</param>
        /// <returns>Regular form of identity</returns>
        private TW_IDENTITY TwidentitylegacyToTwidentity(TW_IDENTITY_LEGACY a_twidentitylegacy)
        {
            TW_IDENTITY twidentity = new TW_IDENTITY();
            twidentity.Id = a_twidentitylegacy.Id;
            twidentity.Manufacturer = a_twidentitylegacy.Manufacturer;
            twidentity.ProductFamily = a_twidentitylegacy.ProductFamily;
            twidentity.ProductName = a_twidentitylegacy.ProductName;
            twidentity.ProtocolMajor = a_twidentitylegacy.ProtocolMajor;
            twidentity.ProtocolMinor = a_twidentitylegacy.ProtocolMinor;
            twidentity.SupportedGroups = a_twidentitylegacy.SupportedGroups;
            twidentity.Version.Country = a_twidentitylegacy.Version.Country;
            twidentity.Version.Info = a_twidentitylegacy.Version.Info;
            twidentity.Version.Language = a_twidentitylegacy.Version.Language;
            twidentity.Version.MajorNum = a_twidentitylegacy.Version.MajorNum;
            twidentity.Version.MinorNum = a_twidentitylegacy.Version.MinorNum;
            return (twidentity);
        }

        /// <summary>
        /// Convert a macosx identity to a public identity...
        /// </summary>
        /// <param name="a_twidentitymacosx">Mac OS X identity to convert</param>
        /// <returns>Regular identity</returns>
        private TW_IDENTITY TwidentitymacosxToTwidentity(TW_IDENTITY_MACOSX a_twidentitymacosx)
        {
            TW_IDENTITY twidentity = new TW_IDENTITY();
            twidentity.Id = a_twidentitymacosx.Id;
            twidentity.Manufacturer = a_twidentitymacosx.Manufacturer;
            twidentity.ProductFamily = a_twidentitymacosx.ProductFamily;
            twidentity.ProductName = a_twidentitymacosx.ProductName;
            twidentity.ProtocolMajor = a_twidentitymacosx.ProtocolMajor;
            twidentity.ProtocolMinor = a_twidentitymacosx.ProtocolMinor;
            twidentity.SupportedGroups = a_twidentitymacosx.SupportedGroups;
            twidentity.Version.Country = a_twidentitymacosx.Version.Country;
            twidentity.Version.Info = a_twidentitymacosx.Version.Info;
            twidentity.Version.Language = a_twidentitymacosx.Version.Language;
            twidentity.Version.MajorNum = a_twidentitymacosx.Version.MajorNum;
            twidentity.Version.MinorNum = a_twidentitymacosx.Version.MinorNum;
            return (twidentity);
        }

        #endregion Private Functions...

        ///////////////////////////////////////////////////////////////////////////////
        // Private Structures, things we need for the thread, and to support stuff
        // like DAT_IMAGENATIVEXFER...
        ///////////////////////////////////////////////////////////////////////////////

        #region Private Structures...

        /// <summary>
        /// The data we share with the thread...
        /// </summary>
        private struct ThreadData
        {
            // The state of the structure...
            public bool blIsInuse;

            public bool blIsComplete;

            // Command...
            public DG dg;

            public DAT dat;
            public MSG msg;
            public STATE stateRollback;
            public bool blRollback;

            // Payload...
            public IntPtr intptrHwnd;

            public IntPtr intptrBitmap;
            public IntPtr twmemref;
            public Image bitmap;
            public UInt32 twuint32;
            public TW_AUDIOINFO twaudioinfo;
            public TW_CALLBACK twcallback;
            public TW_CALLBACK2 twcallback2;
            public TW_CAPABILITY twcapability;
            public TW_CIECOLOR twciecolor;
            public TW_CUSTOMDSDATA twcustomdsdata;
            public TW_DEVICEEVENT twdeviceevent;
            public TW_ENTRYPOINT twentrypoint;
            public TW_EXTIMAGEINFO twextimageinfo;
            public TW_FILESYSTEM twfilesystem;
            public TW_FILTER twfilter;
            public TW_GRAYRESPONSE twgrayresponse;
            public TW_IDENTITY twidentity;
            public TW_IMAGEINFO twimageinfo;
            public TW_IMAGELAYOUT twimagelayout;
            public TW_IMAGEMEMXFER twimagememxfer;
            public TW_JPEGCOMPRESSION twjpegcompression;
            public TW_MEMORY twmemory;
            public TW_PALETTE8 twpalette8;
            public TW_PASSTHRU twpassthru;
            public TW_PENDINGXFERS twpendingxfers;
            public TW_RGBRESPONSE twrgbresponse;
            public TW_SETUPFILEXFER twsetupfilexfer;
            public TW_SETUPMEMXFER twsetupmemxfer;
            public TW_STATUSUTF8 twstatusutf8;
            public TW_USERINTERFACE twuserinterface;

            // Result...
            public STS sts;
        }

        /// <summary>
        /// The Windows Point structure.
        /// Needed for the PreFilterMessage function when we're
        /// handling DAT_EVENT...
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// The Windows MSG structure.
        /// Needed for the PreFilterMessage function when we're
        /// handling DAT_EVENT...
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MESSAGE
        {
            public IntPtr hwnd;
            public UInt32 message;
            public IntPtr wParam;
            public IntPtr lParam;
            public UInt32 time;
            public POINT pt;
        }

        /// <summary>
        /// The header for a Bitmap file.
        /// Needed for supporting DAT.IMAGENATIVEXFER...
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BITMAPFILEHEADER
        {
            public ushort bfType;
            public uint bfSize;
            public ushort bfReserved1;
            public ushort bfReserved2;
            public uint bfOffBits;
        }

        /// <summary>
        /// The header for a Device Independent Bitmap (DIB).
        /// Needed for supporting DAT.IMAGENATIVEXFER...
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;

            public void Init()
            {
                biSize = (uint)Marshal.SizeOf(this);
            }
        }

        /// <summary>
        /// The TIFF file header.
        /// Needed for supporting DAT.IMAGENATIVEXFER...
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TIFFHEADER
        {
            public ushort u8ByteOrder;
            public ushort u16Version;
            public uint u32OffsetFirstIFD;
            public ushort u16u16IFD;
        }

        /// <summary>
        /// An individual TIFF Tag.
        /// Needed for supporting DAT.IMAGENATIVEXFER...
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TIFFTAG
        {
            public ushort u16Tag;
            public ushort u16Type;
            public uint u32Count;
            public uint u32Value;
        }

        [DllImport("user32.dll")]
        private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        #endregion Private Structures...

        ///////////////////////////////////////////////////////////////////////////////
        // Private Attributes...
        ///////////////////////////////////////////////////////////////////////////////

        #region Private Attributes...

        /// <summary>
        /// Our application identity...
        /// </summary>
        private TW_IDENTITY m_twidentityApp;

        private TW_IDENTITY_LEGACY m_twidentitylegacyApp;
        private TW_IDENTITY_MACOSX m_twidentitymacosxApp;

        /// <summary>
        /// Our Data Source identity...
        /// </summary>
        private TW_IDENTITY m_twidentityDs;

        private TW_IDENTITY_LEGACY m_twidentitylegacyDs;
        private TW_IDENTITY_MACOSX m_twidentitymacosxDs;

        /// <summary>
        /// Our current TWAIN state...
        /// </summary>
        private STATE m_state;

        private bool m_blAcceptXferReady;

        /// <summary>
        /// DAT_NULL flags that we've seen after entering into
        /// state 5 through MSG_ENABLEDS or MSG_ENABLEDSUIONLY,
        /// or coming down from DAT_PENDINGXFERS, either
        /// MSG_ENDXFER or MSG_RESET...
        /// </summary>
        private bool m_blIsMsgxferready;

        private bool m_blIsMsgclosedsreq;
        private bool m_blIsMsgclosedsok;
        private bool m_blIsMsgdeviceevent;

        /// <summary>
        /// Automatically issue DAT.STATUS on TWRC_FAILURE...
        /// </summary>
        private bool m_blAutoDatStatus;

        /// <summary>
        /// Windows only, pick between TWAIN_32.DLL and TWAINDSM.DLL...
        /// </summary>
        private bool m_blUseLegacyDSM;

        /// <summary>
        /// Use the callback system (TWAINDSM.DLL only)...
        /// </summary>
        private bool m_blUseCallbacks;

        /// <summary>
        /// The platform we're running on...
        /// </summary>
        private static Platform ms_platform;

        private static bool ms_blFirstPassGetPlatform = true;

        /// <summary>
        /// Delegates for DAT_CALLBACK...
        /// </summary>
        private WindowsDsmEntryCallbackDelegate m_windowsdsmentrycontrolcallbackdelegate;

        private LinuxDsmEntryCallbackDelegate m_linuxdsmentrycontrolcallbackdelegate;
        private MacosxDsmEntryCallbackDelegate m_macosxdsmentrycontrolcallbackdelegate;

        /// <summary>
        /// We only allow one thread at a time to talk to the TWAIN driver...
        /// </summary>
        private Object m_lockTwain;

        /// <summary>
        /// Use this to wait for commands from the caller...
        /// </summary>
        private AutoResetEvent m_autoreseteventCaller;

        /// <summary>
        /// Use this to force the user's command to block until TWAIN has
        /// a response...
        /// </summary>
        private AutoResetEvent m_autoreseteventThread;

        /// <summary>
        /// Use this to force the user's rollback to block until TWAIN has
        /// a response...
        /// </summary>
        private AutoResetEvent m_autoreseteventRollback;

        /// <summary>
        /// One can get into a race condition with the thread, so we use
        /// this event to confirm that it's started and ready for use...
        /// </summary>
        private AutoResetEvent m_autoreseteventThreadStarted;

        /// <summary>
        /// The data we share with the thread...
        /// </summary>
        //private ThreadData m_threaddata;

        /// <summary>
        /// Our callback for device events...
        /// </summary>
        private DeviceEventCallback m_deviceeventcallback;

        /// <summary>
        /// Our callback function for scanning...
        /// </summary>
        private ScanCallback m_scancallback;

        /// <summary>
        /// Run stuff in a caller's UI thread...
        /// </summary>
        private RunInUiThreadDelegate m_runinuithreaddelegate;

        /// <summary>
        /// The event calls don't go through the thread...
        /// </summary>
        private TW_EVENT m_tweventPreFilterMessage;

        // Remember the window handle, so we can reuse it...
        private IntPtr m_intptrHwnd;

        /// <summary>
        /// Our thread...
        /// </summary>
        private Thread m_threadTwain;

        /// <summary>
        /// How we talk to our thread...
        /// </summary>
        private TwainCommand m_twaincommand;

        /// <summary>
        ///  Indecies for commands that have to do something a
        ///  bit more fancy, such as running the command in the
        ///  context of a GUI thread...
        /// </summary>
        private long m_lIndexDatImagefilexfer;

        private long m_lIndexDatImagememfilexfer;
        private long m_lIndexDatImagememxfer;
        private long m_lIndexDatImagenativexfer;
        private long m_lIndexDatUserinterface;

        /// <summary>
        /// Our helper functions from the DSM...
        /// </summary>
        private TW_ENTRYPOINT_DELEGATES m_twentrypointdelegates;

        /// <summary>
        /// Remember the user interface settings for DISABLEDS...
        /// </summary>
        private TW_USERINTERFACE m_twuserinterface;

        #endregion Private Attributes...

        ///////////////////////////////////////////////////////////////////////////////
        // Private P/Invokes...
        ///////////////////////////////////////////////////////////////////////////////

        #region Private P/Invokes...

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        #endregion Private P/Invokes...

        ///////////////////////////////////////////////////////////////////////////////
        // TwainCommand...
        ///////////////////////////////////////////////////////////////////////////////

        #region TwainCommand...

        /// <summary>
        /// We have TWAIN commands that can be called by the application from any
        /// thread they want.  We do a lock, and then build a command and submit
        /// it to the Main thread.  The Main thread runs without locks, so all it
        /// is allowed to do is examine TwainCommands to see if it has work.  If
        /// it finds an item, it takes care of it, and changes it to complete.
        /// </summary>
        private sealed class TwainCommand
        {
            ///////////////////////////////////////////////////////////////////////////
            // Public Functions...
            ///////////////////////////////////////////////////////////////////////////

            #region Public Functions...

            /// <summary>
            /// Initialize an array that we'll be sharing between the TWAIN operations
            /// and the Main thread...
            /// </summary>
            public TwainCommand()
            {
                m_athreaddata = new ThreadData[8];
            }

            /// <summary>
            /// Complete a command
            /// </summary>
            /// <param name="a_lIndex">index to update</param>
            /// <param name="a_threaddata">data to use</param>
            public void Complete(long a_lIndex, ThreadData a_threaddata)
            {
                // If we're out of bounds, return an empty structure...
                if ((a_lIndex < 0) || (a_lIndex >= m_athreaddata.Length))
                {
                    return;
                }

                // We're not really a command...
                if (!m_athreaddata[a_lIndex].blIsInuse)
                {
                    return;
                }

                // Do the update and tag it complete...
                m_athreaddata[a_lIndex] = a_threaddata;
                m_athreaddata[a_lIndex].blIsComplete = true;
            }

            /// <summary>
            /// Delete a command...
            /// </summary>
            /// <returns>the requested command</returns>
            public void Delete(long a_lIndex)
            {
                // If we're out of bounds, return an empty structure...
                if ((a_lIndex < 0) || (a_lIndex >= m_athreaddata.Length))
                {
                    return;
                }

                // Clear the record...
                m_athreaddata[a_lIndex] = default(ThreadData);
            }

            /// <summary>
            /// Get a command...
            /// </summary>
            /// <returns>the requested command</returns>
            public ThreadData Get(long a_lIndex)
            {
                // If we're out of bounds, return an empty structure...
                if ((a_lIndex < 0) || (a_lIndex >= m_athreaddata.Length))
                {
                    return (new ThreadData());
                }

                // Return what we found...
                return (m_athreaddata[a_lIndex]);
            }

            /// <summary>
            /// Get the next command in the list...
            /// </summary>
            /// <param name="a_lIndex">the index of the data</param>
            /// <param name="a_threaddata">the command we'll return</param>
            /// <returns>true if we found something</returns>
            public bool GetNext(out long a_lIndex, out ThreadData a_threaddata)
            {
                long lIndex;

                // Init stuff...
                lIndex = m_lIndex;
                a_lIndex = 0;
                a_threaddata = default(ThreadData);

                // Cycle once through the commands to see if we have any...
                for (;;)
                {
                    // We found something, copy it out, point to the next
                    // item (so we know we're looking at the whole list)
                    // and return...
                    if (m_athreaddata[lIndex].blIsInuse && !m_athreaddata[lIndex].blIsComplete)
                    {
                        a_threaddata = m_athreaddata[lIndex];
                        a_lIndex = lIndex;
                        m_lIndex = lIndex + 1;
                        if (m_lIndex >= m_athreaddata.Length)
                        {
                            m_lIndex = 0;
                        }
                        return (true);
                    }

                    // Next item...
                    lIndex += 1;
                    if (lIndex >= m_athreaddata.Length)
                    {
                        lIndex = 0;
                    }

                    // We've cycled, and we didn't find anything...
                    if (lIndex == m_lIndex)
                    {
                        a_lIndex = lIndex;
                        return (false);
                    }
                }
            }

            /// <summary>
            /// Submit a new command...
            /// </summary>
            /// <returns></returns>
            public long Submit(ThreadData a_threadata)
            {
                long ll;

                // We won't leave until we've submitted the beastie...
                for (;;)
                {
                    // Look for a free slot...
                    for (ll = 0; ll < m_athreaddata.Length; ll++)
                    {
                        if (!m_athreaddata[ll].blIsInuse)
                        {
                            m_athreaddata[ll] = a_threadata;
                            m_athreaddata[ll].blIsInuse = true;
                            return (ll);
                        }
                    }

                    // Wait a little...
                    Thread.Sleep(0);
                }
            }

            /// <summary>
            /// Update a command
            /// </summary>
            /// <param name="a_lIndex">index to update</param>
            /// <param name="a_threaddata">data to use</param>
            public void Update(long a_lIndex, ThreadData a_threaddata)
            {
                // If we're out of bounds, return an empty structure...
                if ((a_lIndex < 0) || (a_lIndex >= m_athreaddata.Length))
                {
                    return;
                }

                // We're not really a command...
                if (!m_athreaddata[a_lIndex].blIsInuse)
                {
                    return;
                }

                // Do the update...
                m_athreaddata[a_lIndex] = a_threaddata;
            }

            #endregion Public Functions...

            ///////////////////////////////////////////////////////////////////////////
            // Private Attributes...
            ///////////////////////////////////////////////////////////////////////////

            #region Private Attributes...

            /// <summary>
            /// The data we're sharing.  A null in a position means its available for
            /// use.  The Main thread only consumes items, it never creates or
            /// destroys them, that's done by the various commands.
            /// </summary>
            private ThreadData[] m_athreaddata;

            /// <summary>
            /// Index for browsing m_athreaddata for work...
            /// </summary>
            private long m_lIndex;

            #endregion Private Attributes...
        }

        #endregion TwainCommand...
    }

    /// <summary>
    /// A quick and dirty CSV reader/writer...
    /// </summary>
    public class CSV
    {
        ///////////////////////////////////////////////////////////////////////////////
        // Public Functions...
        ///////////////////////////////////////////////////////////////////////////////

        #region Public Functions...

        /// <summary>
        /// Start with an empty string...
        /// </summary>
        public CSV()
        {
            m_szCsv = "";
        }

        /// <summary>
        /// Add an item to a CSV string...
        /// </summary>
        /// <param name="a_szItem">Something to add to the CSV string</param>
        public void Add(string a_szItem)
        {
            // If the item has commas, we need to do work...
            if (a_szItem.Contains(","))
            {
                // If the item has quotes, replace them with paired quotes, then
                // quote it and add it...
                if (a_szItem.Contains("\""))
                {
                    m_szCsv += ((m_szCsv != "") ? "," : "") + "\"" + a_szItem.Replace("\"", "\"\"") + "\"";
                }

                // Otherwise, just quote it and add it...
                else
                {
                    m_szCsv += ((m_szCsv != "") ? "," : "") + "\"" + a_szItem + "\"";
                }
            }

            // If the item has quotes, replace them with escaped quotes, then
            // quote it and add it...
            else if (a_szItem.Contains("\""))
            {
                m_szCsv += ((m_szCsv != "") ? "," : "") + "\"" + a_szItem.Replace("\"", "\"\"") + "\"";
            }

            // Otherwise, just add it...
            else
            {
                m_szCsv += ((m_szCsv != "") ? "," : "") + a_szItem;
            }
        }

        /// <summary>
        /// Clear the record...
        /// </summary>
        public void Clear()
        {
            m_szCsv = "";
        }

        /// <summary>
        /// Get the current CSV string...
        /// </summary>
        /// <returns>The current value of the CSV string</returns>
        public string Get()
        {
            return (m_szCsv);
        }

        /// <summary>
        /// Parse a CSV string...
        /// </summary>
        /// <param name="a_szCsv">A CSV string to parse</param>
        /// <returns>An array if items (some can be CSV themselves)</returns>
        public static string[] Parse(string a_szCsv)
        {
            int ii;
            bool blEnd;
            string[] aszCsv;
            string[] aszLeft;
            string[] aszRight;

            // Validate...
            if ((a_szCsv == null) || (a_szCsv == ""))
            {
                return (new string[] { "" });
            }

            // If there are no quotes, then parse it fast...
            if (!a_szCsv.Contains("\""))
            {
                return (a_szCsv.Split(new char[] { ',' }));
            }

            // There's no opening quote, so split and recurse...
            if (a_szCsv[0] != '"')
            {
                aszLeft = new string[] { a_szCsv.Substring(0, a_szCsv.IndexOf(',')) };
                aszRight = Parse(a_szCsv.Remove(0, a_szCsv.IndexOf(',') + 1));
                aszCsv = new string[aszLeft.Length + aszRight.Length];
                aszLeft.CopyTo(aszCsv, 0);
                aszRight.CopyTo(aszCsv, aszLeft.Length);
                return (aszCsv);
            }

            // Handle the quoted string...
            else
            {
                // Find the terminating quote...
                blEnd = true;
                for (ii = 0; ii < a_szCsv.Length; ii++)
                {
                    if (a_szCsv[ii] == '"')
                    {
                        blEnd = !blEnd;
                    }
                    else if (blEnd && (a_szCsv[ii] == ','))
                    {
                        break;
                    }
                }
                ii -= 1;

                // We have a problem...
                if (!blEnd)
                {
                    throw new Exception("Error in CSV string...");
                }

                // This is the last item, remove any escaped quotes and return it...
                if (((ii + 1) >= a_szCsv.Length))
                {
                    return (new string[] { a_szCsv.Substring(1, a_szCsv.Length - 2).Replace("\"\"", "\"") });
                }

                // We have more data...
                if (a_szCsv[ii + 1] == ',')
                {
                    aszLeft = new string[] { a_szCsv.Substring(1, ii - 1).Replace("\"\"", "\"") };
                    aszRight = Parse(a_szCsv.Remove(0, ii + 2));
                    aszCsv = new string[aszLeft.Length + aszRight.Length];
                    aszLeft.CopyTo(aszCsv, 0);
                    aszRight.CopyTo(aszCsv, aszLeft.Length);
                    return (aszCsv);
                }

                // We have a problem...
                throw new Exception("Error in CSV string...");
            }
        }

        #endregion Public Functions...

        ///////////////////////////////////////////////////////////////////////////////
        // Private Attributes...
        ///////////////////////////////////////////////////////////////////////////////

        #region Private Attributes...

        /// <summary>
        /// Our working string for creating or parsing...
        /// </summary>
        private string m_szCsv;

        #endregion Private Attributes...
    }

    /// <summary>
    /// Our logger.  If we bump up to 4.5 (and if mono supports it at compile
    /// time), then we'll be able to add the following to our traces, which
    /// seems like it should be more than enough to locate log messages.  For
    /// now we'll leave the log messages undecorated:
    ///     [CallerFilePath] string file = "",
    ///     [CallerMemberName] string member = "",
    ///     [CallerLineNumber] int line = 0
    /// </summary>
    public static class Log
    {
        // Public Methods...

        #region Public Methods...

        /// <summary>
        /// Initialize our delegates...
        /// </summary>
        static Log()
        {
            Close = CloseLocal;
            GetLevel = GetLevelLocal;
            Open = OpenLocal;
            RegisterTwain = RegisterTwainLocal;
            SetFlush = SetFlushLocal;
            SetLevel = SetLevelLocal;
            WriteEntry = WriteEntryLocal;
        }

        /// <summary>
        /// Let the caller override our delegates with their own functions...
        /// </summary>
        /// <param name="a_closedelegate">use this to close the logging session</param>
        /// <param name="a_getleveldelegate">get the current log level</param>
        /// <param name="a_opendelegate">open the logging session</param>
        /// <param name="a_registertwaindelegate">not needed at this time</param>
        /// <param name="a_setflushdelegate">turn flushing on and off</param>
        /// <param name="a_setleveldelegate">set the new log level</param>
        /// <param name="a_writeentrydelegate">the function that actually writes to the log</param>
        /// <param name="a_getstatedelegate">returns a way to get the current TWAIN state</param>
        public static void Override
        (
            CloseDelegate a_closedelegate,
            GetLevelDelegate a_getleveldelegate,
            OpenDelegate a_opendelegate,
            RegisterTwainDelegate a_registertwaindelegate,
            SetFlushDelegate a_setflushdelegate,
            SetLevelDelegate a_setleveldelegate,
            WriteEntryDelegate a_writeentrydelegate,
            out GetStateDelegate a_getstatedelegate
        )
        {
            Close = (a_closedelegate != null) ? a_closedelegate : CloseLocal;
            GetLevel = (a_getleveldelegate != null) ? a_getleveldelegate : GetLevelLocal;
            Open = (a_opendelegate != null) ? a_opendelegate : OpenLocal;
            RegisterTwain = (a_registertwaindelegate != null) ? a_registertwaindelegate : RegisterTwainLocal;
            SetFlush = (a_setflushdelegate != null) ? a_setflushdelegate : SetFlushLocal;
            SetLevel = (a_setleveldelegate != null) ? a_setleveldelegate : SetLevelLocal;
            WriteEntry = (a_writeentrydelegate != null) ? a_writeentrydelegate : WriteEntryLocal;
            a_getstatedelegate = GetStateLocal;
        }

        /// <summary>
        /// Write an assert message, but only throw with a debug build...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Assert(string a_szMessage)
        {
            WriteEntry("A", a_szMessage, true);
#if DEBUG
            throw new Exception(a_szMessage);
#endif
        }

        /// <summary>
        /// Write an error message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Error(string a_szMessage)
        {
            WriteEntry("E", a_szMessage, true);
        }

        /// <summary>
        /// Write an informational message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Info(string a_szMessage)
        {
            WriteEntry(".", a_szMessage, true);
        }

        /// <summary>
        /// Log after sending to the TWAIN driver...
        /// </summary>
        /// <param name="a_szSts">status</param>
        /// <param name="a_szMemref">data</param>
        public static void LogSendAfter(string a_szSts, string a_szMemref)
        {
            if ((a_szMemref != null) && (a_szMemref != "") && (a_szMemref[0] != '('))
            {
                Log.Info("twn> " + a_szMemref);
            }
            Log.Info("twn> " + a_szSts);
        }

        /// <summary>
        /// Log before sending to the TWAIN driver...
        /// </summary>
        /// <param name="a_szDg">data group</param>
        /// <param name="a_szDat">data argument type</param>
        /// <param name="a_szMsg">message</param>
        /// <param name="a_szMemref">data</param>
        public static void LogSendBefore(string a_szDg, string a_szDat, string a_szMsg, string a_szMemref)
        {
            Log.Info("");
            Log.Info("twn> DG_" + a_szDg + "/DAT_" + a_szDat + "/MSG_" + a_szMsg);
            if ((a_szMemref != null) && (a_szMemref != "") && (a_szMemref[0] != '('))
            {
                Log.Info("twn> " + a_szMemref);
            }
        }

        /// <summary>
        /// Write an warning message...
        /// </summary>
        /// <param name="a_szMessage">message to log</param>
        public static void Warn(string a_szMessage)
        {
            WriteEntry("W", a_szMessage, true);
        }

        #endregion Public Methods...

        // Public Definitions...

        #region Public Definitions...

        // The public methods that need attributes, here offered
        // as delegates, so that a caller will be able to override
        // them...
        public delegate void CloseDelegate();

        public delegate int GetLevelDelegate();

        public delegate string GetStateDelegate();

        public delegate void OpenDelegate(string a_szName, string a_szPath, int a_iLevel);

        public delegate void RegisterTwainDelegate(TWAIN a_twain);

        public delegate void SetFlushDelegate(bool a_blFlush);

        public delegate void SetLevelDelegate(int a_iLevel);

        public delegate void WriteEntryDelegate(string a_szSeverity, string a_szMessage, bool a_blFlush);

        #endregion Public Definitions...

        // Public Attributes...

        #region Public Attributes...

        // The public methods that need attributes, here offered
        // as delegates, so that a caller will be able to override
        // them...
        public static CloseDelegate Close;

        public static GetLevelDelegate GetLevel;
        public static OpenDelegate Open;
        public static RegisterTwainDelegate RegisterTwain;
        public static SetFlushDelegate SetFlush;
        public static SetLevelDelegate SetLevel;
        public static WriteEntryDelegate WriteEntry;

        #endregion Public Attributes...

        // Public Definitions...

        #region Private Definitions

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        #endregion Private Definitions

        // Private Methods...

        #region Private Methods...

        /// <summary>
        /// Close tracing...
        /// </summary>
        private static void CloseLocal()
        {
            if (!ms_blFirstPass)
            {
                Trace.Close();
                //ms_filestream.Close();
                ms_filestream = null;
            }
            ms_blFirstPass = true;
            ms_blOpened = false;
            ms_blFlush = false;
            ms_iMessageNumber = 0;
        }

        /// <summary>
        /// Get the debugging level...
        /// </summary>
        /// <returns>the level</returns>
        private static int GetLevelLocal()
        {
            return (ms_iLevel);
        }

        /// <summary>
        /// Get the state...
        /// </summary>
        /// <returns>the level</returns>
        private static string GetStateLocal()
        {
            return ((ms_twain == null) ? "S0" : ms_twain.GetState().ToString());
        }

        /// <summary>
        /// Turn on the listener for our log file...
        /// </summary>
        /// <param name="a_szName">the name of our log</param>
        /// <param name="a_szPath">the path where we want our log to go</param>
        /// <param name="a_iLevel">debug level</param>
        private static void OpenLocal(string a_szName, string a_szPath, int a_iLevel)
        {
            // Init stuff...
            ms_blFirstPass = true;
            ms_blOpened = true;
            ms_blFlush = false;
            ms_iMessageNumber = 0;
            ms_iLevel = a_iLevel;

            // Ask for a TWAINDSM log...
            if (a_iLevel > 0)
            {
                Environment.SetEnvironmentVariable("TWAINDSM_LOG", Path.Combine(a_szPath, "twaindsm.log"));
                Environment.SetEnvironmentVariable("TWAINDSM_MODE", "w");
            }

            // Turn on the listener...
            ms_filestream = File.Open(Path.Combine(a_szPath, a_szName + ".log"), FileMode.Append, FileAccess.Write, FileShare.Read);
            Trace.Listeners.Add(new TextWriterTraceListener(ms_filestream, a_szName + "Listener"));
        }

        /// <summary>
        /// Register the TWAIN object so we can get some extra info...
        /// </summary>
        /// <param name="a_twain">twain object or null</param>
        private static void RegisterTwainLocal(TWAIN a_twain)
        {
            ms_twain = a_twain;
        }

        /// <summary>
        /// Flush data to the file...
        /// </summary>
        private static void SetFlushLocal(bool a_blFlush)
        {
            ms_blFlush = a_blFlush;
            if (a_blFlush)
            {
                Trace.Flush();
            }
        }

        /// <summary>
        /// Set the debugging level
        /// </summary>
        /// <param name="a_iLevel"></param>
        private static void SetLevelLocal(int a_iLevel)
        {
            ms_iLevel = a_iLevel;
        }

        /// <summary>
        /// Do this for all of them...
        /// </summary>
        /// <param name="a_szMessage">The message</param>
        /// <param name="a_szSeverity">Message severity</param>
        /// <param name="a_blFlush">Flush it to disk</param>
        private static void WriteEntryLocal(string a_szSeverity, string a_szMessage, bool a_blFlush)
        {
            long lThreadId;

            // Get our thread id...
            if (ms_blIsWindows)
            {
                lThreadId = GetCurrentThreadId();
            }
            else
            {
                lThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            // First pass...
            if (ms_blFirstPass)
            {
                string szPlatform;

                // We're Windows...

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    szPlatform = "windows";
                }

                // We're Mac OS X (this has to come before LINUX!!!)...
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    szPlatform = "macosx";
                }

                // We're Linux...
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    szPlatform = "linux";
                }

                // We have a problem, Log will throw for us...
                else
                {
                    szPlatform = "unknown";
                }
                if (!ms_blOpened)
                {
                    // We'll assume they want logging, since they didn't tell us...
                    Open("Twain", ".", 1);
                }
                Trace.UseGlobalLock = true;
                ms_blFirstPass = false;
                Trace.WriteLine
                (
                    string.Format
                    (
                        "{0:D6} {1} {2} T{3:D8} V{4} ts:{5} os:{6}",
                        ms_iMessageNumber++,
                        DateTime.Now.ToString("HHmmssffffff"),
                        (ms_twain != null) ? ms_twain.GetState().ToString() : "S0",
                        lThreadId,
                        a_szSeverity.ToString(),
                        DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.ffffff"),
                        szPlatform
                    )
                );
            }

            // And log it...
            Trace.WriteLine
            (
                string.Format
                (
                    "{0:D6} {1} {2} T{3:D8} V{4} {5}",
                    ms_iMessageNumber++,
                    DateTime.Now.ToString("HHmmssffffff"),
                    (ms_twain != null) ? ms_twain.GetState().ToString() : "S0",
                    lThreadId,
                    a_szSeverity.ToString(),
                    a_szMessage
                )
            );

            // Flush it...
            if (a_blFlush)
            {
                Trace.Flush();
            }
        }

        #endregion Private Methods...

        // Private Attributes...

        #region Private Attributes

        private static bool ms_blFirstPass = true;
        private static bool ms_blOpened = false;
        private static bool ms_blFlush = false;
        private static int ms_iMessageNumber = 0;
        private static int ms_iLevel = 0;
        private static TWAIN ms_twain = null;
        private static bool ms_blIsWindows = false;
        private static FileStream ms_filestream;

        #endregion Private Attributes
    }
}