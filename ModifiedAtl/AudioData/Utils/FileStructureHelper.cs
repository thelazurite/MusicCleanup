﻿using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.AudioData
{
    /// <summary>
    /// Helper class used to :
    ///   - Record location and size of specific chunks of data within a structured file, called "Zones"
    ///   - Record location, value and type of headers describing Zones
    ///   - Modify these headers as Zones appear, disappear, expand or shrink
    /// </summary>
    public class FileStructureHelper
    {
        public const String DEFAULT_ZONE_NAME = "default"; // Default zone name to be used when no naming is necessary (simple cases where there is a but a single Zone to describe)

        // Type of action to react to
        public const Int32 ACTION_EDIT    = 0; // Existing zone is edited, and not removed
        public const Int32 ACTION_ADD     = 1; // New zone is added
        public const Int32 ACTION_DELETE  = 2; // Existing zone is removed

        /// <summary>
        /// Container class describing a frame header
        /// </summary>
        public class FrameHeader
        {
            // Header types
            public const Byte TYPE_COUNTER = 0;  // Counter : counts the underlying number of frames
            public const Byte TYPE_SIZE = 1;     // Size : documents the size of a given frame / group of frames
            public const Byte TYPE_INDEX = 2;    // Index (absolute) : documents the offset (position of 1st byte) of a given frame
            public const Byte TYPE_RINDEX = 3;   // Index (relative) : documents the offset (position of 1st byte) of a given frame, relative to the header's position

            /// <summary>
            /// Header type (allowed values are TYPE_XXX within FrameHeader class)
            /// </summary>
            public Byte Type;
            /// <summary>
            /// Position of the header
            /// </summary>
            public Int64 Position;
            /// <summary>
            /// Current value of the header (counter : number of frames / size : frame size / index : frame index (absolute) / rindex : frame index (relative to header position))
            /// </summary>
            public Object Value;
            /// <summary>
            /// True if header value is stored using little-endian convention; false if big-endian
            /// </summary>
            public Boolean IsLittleEndian;

            /// <summary>
            /// Constructs a new frame header using the given field values
            /// </summary>
            public FrameHeader(Byte type, Int64 position, Object value, Boolean isLittleEndian = true)
            {
                Type = type;  Position = position; Value = value; IsLittleEndian = isLittleEndian;
            }
        }

        /// <summary>
        /// Container class describing a chunk/frame within a structured file 
        /// </summary>
        public class Zone
        {
            /// <summary>
            /// Zone name (any unique value will do; used as internal reference only)
            /// </summary>
            public String Name;
            /// <summary>
            /// Offset in bytes
            /// </summary>
            public Int64 Offset;
            /// <summary>
            /// Size in bytes
            /// </summary>
            public Int32 Size;
            /// <summary>
            /// Data sequence that has to be written in the zone when the zone does not contain any other data
            /// </summary>
            public Byte[] CoreSignature;
            /// <summary>
            /// Generic usage flag for storing information
            /// </summary>
            public Byte Flag;
            /// <summary>
            /// Size descriptors and item counters referencing the zone elsehwere on the file
            /// </summary>
            public IList<FrameHeader> Headers;

            /// <summary>
            /// Construct a new Zone using the given field values
            /// </summary>
            public Zone(String name, Int64 offset, Int32 size, Byte[] coreSignature, Byte flag = 0)
            {
                Name = name; Offset = offset; Size = size; CoreSignature = coreSignature; Flag = flag;
                Headers = new List<FrameHeader>();
            }

            /// <summary>
            /// Remove all headers
            /// </summary>
            public void Clear()
            {
                if (Headers != null) Headers.Clear();
            }
        }

        // Recorded zones
        private IDictionary<String, Zone> zones;
        
        // Stores offset variations caused by zone editing (add/remove/shrink/expand) within current file
        //      Dictionary key  : zone name
        //      KVP Key         : initial end offset of given zone (i.e. position of last byte within zone)
        //      KVP Value       : variation applied to given zone (can be positive or negative)
        private IDictionary<String,KeyValuePair<Int64, Int64>> dynamicOffsetCorrection = new Dictionary<String,KeyValuePair<Int64,Int64>>();
        
        // True if attached file uses little-endian convention for number representation; false if big-endian
        private Boolean isLittleEndian;


        /// <summary>
        /// Names of recorded zones
        /// </summary>
        public ICollection<String> ZoneNames => zones.Keys;

        /// <summary>
        /// Recorded zones
        /// </summary>
        public ICollection<Zone> Zones => zones.Values;


        /// <summary>
        /// Construct a new FileStructureHelper
        /// </summary>
        /// <param name="isLittleEndian">True if unerlying file uses little-endian convention for number representation; false if big-endian</param>
        public FileStructureHelper(Boolean isLittleEndian = true)
        {
            this.isLittleEndian = isLittleEndian;
            zones = new Dictionary<String, Zone>();
        }

        /// <summary>
        /// Clears all recorded Zones
        /// </summary>
        public void Clear()
        {
            if (null != zones)
            {
                foreach(var s in zones.Keys)
                {
                    zones[s].Clear();
                }
                zones.Clear();
            }
            dynamicOffsetCorrection.Clear();
        }

        /// <summary>
        /// Retrieve a zone by its name
        /// </summary>
        /// <param name="name">Name of the zone to retrieve</param>
        /// <returns>The zone corresponding to the given name; null if not found</returns>
        public Zone GetZone(String name)
        {
            if (zones.ContainsKey(name)) return zones[name]; else return null;
        }

        /// <summary>
        /// Record a new zone by copying the given zone
        /// </summary>
        /// <param name="zone">Zone to be recorded</param>
        public void AddZone(Zone zone)
        {
            AddZone(zone.Offset, zone.Size, zone.CoreSignature, zone.Name);

            foreach (var header in zone.Headers)
            {
                addZoneHeader(zone.Name, header.Type, header.Position, header.Value, header.IsLittleEndian);
            }
        }

        /// <summary>
        /// Record a new zone using the given fields
        /// </summary>
        public void AddZone(Int64 offset, Int32 size, String name = DEFAULT_ZONE_NAME)
        {
            AddZone(offset, size, new Byte[0], name);
        }

        /// <summary>
        /// Record a new zone using the given fields
        /// </summary>
        public void AddZone(Int64 offset, Int32 size, Byte[] coreSignature, String zone = DEFAULT_ZONE_NAME)
        {
            if (!zones.ContainsKey(zone))
            {
                zones.Add(zone, new Zone(zone, offset, size, coreSignature));
            }
            else // Existing zone might already contain headers
            {
                zones[zone].Name = zone;
                zones[zone].Offset = offset;
                zones[zone].Size = size;
                zones[zone].CoreSignature = coreSignature;
            }
        }

        /// <summary>
        /// Record a new Counter-type header using the given fields and attach it to the zone of given name
        /// </summary>
        public void AddCounter(Int64 position, Object value, String zone = DEFAULT_ZONE_NAME)
        {
            addZoneHeader(zone, FrameHeader.TYPE_COUNTER, position, value, isLittleEndian);
        }

        /// <summary>
        /// Record a new Size-type header using the given fields and attach it to the zone of given name
        /// </summary>
        public void AddSize(Int64 position, Object value, String zone = DEFAULT_ZONE_NAME)
        {
            addZoneHeader(zone, FrameHeader.TYPE_SIZE, position, value, isLittleEndian);
        }

        /// <summary>
        /// Record a new Index-type header using the given fields and attach it to the zone of given name
        /// </summary>
        public void AddIndex(Int64 position, Object value, Boolean relative = false, String zone = DEFAULT_ZONE_NAME)
        {
            addZoneHeader(zone, relative? FrameHeader.TYPE_RINDEX : FrameHeader.TYPE_INDEX, position, value, isLittleEndian);
        }

        /// <summary>
        /// Record a new header using the given fields and attach it to the zone of given name
        /// </summary>
        private void addZoneHeader(String zone, Byte type, Int64 position, Object value, Boolean isLittleEndian)
        {
            if (!zones.ContainsKey(zone)) // Might happen when reading header frames of containing upper frames, without having reached tag frame itself
            {
                AddZone(0, 0, zone);
            }
            zones[zone].Headers.Add(new FrameHeader(type, position, value, isLittleEndian));
        }

        /// <summary>
        /// Update all headers at the given position to the given value
        /// (useful when multiple zones refer to the very same header)
        /// </summary>
        /// <param name="position">Position of header to be updated</param>
        /// <param name="newValue">New value to be assigned to header</param>
        private void updateAllHeadersAtPosition(Int64 position, Object newValue)
        {
            // NB : this method should perform quite badly -- evolve to using position-based dictionary if any performance issue arise
            foreach (var frame in zones.Values)
            {
                foreach (var header in frame.Headers)
                {
                    if (position == header.Position)
                    {
                        header.Value = newValue;
                    }
                }
            }
        }

        /// <summary>
        /// Perform the addition between the two given values and encodes the result to an array of bytes, according to the type of the reference value
        /// </summary>
        /// <param name="value">Reference value</param>
        /// <param name="delta">Value to add</param>
        /// <param name="updatedValue">Updated value (out parameter; will be returned as same type as reference value)</param>
        /// <returns>Resulting value after the addition, encoded into an array of bytes, as the same type of the reference value</returns>
        private static Byte[] addToValue(Object value, Int32 delta, out Object updatedValue)
        {
            if (value is Byte)
            {
                updatedValue = (Byte)((Byte)value + delta);
                return new Byte[1] { (Byte)updatedValue };
            }
            else if (value is Int16)
            {
                updatedValue = (Int16)((Int16)value + delta);
                return BitConverter.GetBytes((Int16)updatedValue);
            }
            else if (value is UInt16)
            {
                updatedValue = (UInt16)((UInt16)value + delta);
                return BitConverter.GetBytes((UInt16)updatedValue);
            }
            else if (value is Int32)
            {
                updatedValue = (Int32)((Int32)value + delta);
                return BitConverter.GetBytes((Int32)updatedValue);
            }
            else if (value is UInt32)
            {
                updatedValue = (UInt32)((UInt32)value + delta);
                return BitConverter.GetBytes((UInt32)updatedValue);
            }
            else if (value is Int64)
            {
                updatedValue = (Int64)((Int64)value + delta);
                return BitConverter.GetBytes((Int64)updatedValue);
            }
            else if (value is UInt64) // Need to tweak because ulong + int is illegal according to the compiler
            {
                if (delta > 0)
                {
                    updatedValue = (UInt64)value + (UInt64)delta;
                }
                else
                {
                    updatedValue = (UInt64)value - (UInt64)(-delta);
                }
                return BitConverter.GetBytes((UInt64)updatedValue);
            }
            else
            {
                updatedValue = value;
                return null;
            }
        }

        /// <summary>
        /// Rewrite all zone headers in the given stream according to the given size evolution and the given action
        /// </summary>
        /// <param name="w">Stream to write modifications to</param>
        /// <param name="deltaSize">Evolution of zone size (in bytes; positive or negative)</param>
        /// <param name="action">Action applied to zone</param>
        /// <param name="zone">Name of zone</param>
        /// <returns></returns>
        public Boolean RewriteHeaders(BinaryWriter w, Int32 deltaSize, Int32 action, String zone = DEFAULT_ZONE_NAME)
        {
            var result = true;
            Int32 delta;
            Int64 offsetCorrection;
            Byte[] value;
            Object updatedValue;

            if (zones != null && zones.ContainsKey(zone))
            {
                foreach (var header in zones[zone].Headers)
                {
                    offsetCorrection = 0;
                    delta = 0;
                    foreach(var offsetDelta in dynamicOffsetCorrection.Values)
                    {
                        if (header.Position >= offsetDelta.Key) offsetCorrection += offsetDelta.Value;
                    }

                    if (FrameHeader.TYPE_COUNTER == header.Type)
                    {
                        switch (action)
                        {
                            case ACTION_ADD: delta = 1; break;
                            case ACTION_DELETE: delta = -1; break;
                            default: delta = 0; break;
                        }

                    }
                    else if (FrameHeader.TYPE_SIZE == header.Type)
                    {
                        delta = deltaSize;
                        if (!dynamicOffsetCorrection.ContainsKey(zone))
                        {
                            dynamicOffsetCorrection.Add(zone, new KeyValuePair<Int64, Int64>(zones[zone].Offset + zones[zone].Size, deltaSize));
                        }
                    }

                    if ((FrameHeader.TYPE_COUNTER == header.Type || FrameHeader.TYPE_SIZE == header.Type) && (delta != 0))
                    {
                        w.BaseStream.Seek(header.Position + offsetCorrection, SeekOrigin.Begin);

                        value = addToValue(header.Value, delta, out updatedValue);

                        if (null == value) throw new NotSupportedException("Value type not supported for " + zone + "@" + header.Position + " : " + header.Value.GetType());

                        // The very same frame header is referenced from another frame and must be updated to its new value
                        updateAllHeadersAtPosition(header.Position, updatedValue);

                        if (!header.IsLittleEndian) Array.Reverse(value);

                        w.Write(value);
                    }
                    else if (FrameHeader.TYPE_INDEX == header.Type || FrameHeader.TYPE_RINDEX == header.Type)
                    {
                        var headerPosition = header.Position + offsetCorrection;
                        w.BaseStream.Seek(headerPosition, SeekOrigin.Begin);
                        value = null;

                        var headerOffsetCorrection = (FrameHeader.TYPE_RINDEX == header.Type) ? headerPosition : 0;

                        if (action != ACTION_DELETE)
                        {
                            if (header.Value is Int64)
                            {
                                value = BitConverter.GetBytes((Int64)zones[zone].Offset + offsetCorrection - headerOffsetCorrection);
                            }
                            else if (header.Value is Int32)
                            {
                                value = BitConverter.GetBytes((Int32)(zones[zone].Offset + offsetCorrection - headerOffsetCorrection));
                            }

                            if (!header.IsLittleEndian) Array.Reverse(value);
                        }
                        else
                        {
                            if (header.Value is Int64)
                            {
                                value = BitConverter.GetBytes((Int64)0);
                            }
                            else if (header.Value is Int32)
                            {
                                value = BitConverter.GetBytes((Int32)0);
                            }
                        }

                        if (null == value) throw new NotSupportedException("Value type not supported for index in " + zone + "@" + header.Position + " : " + header.Value.GetType());

                        w.Write(value);
                    }
                }
            }

            return result;
        }

    }
}