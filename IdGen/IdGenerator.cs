﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IdGen
{
    /// <summary>
    /// Generates Id's inspired by Twitter's (late) Snowflake project.
    /// </summary>
    public class IdGenerator : IIdGenerator<long>
    {
        private static readonly DateTime defaultepoch = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly ITimeSource defaulttimesource = new DefaultTimeSource();

        private int _sequence = 0;
        private long _lastgen = -1;

        private readonly DateTime _epoch;
        private readonly MaskConfig _maskconfig;
        private readonly int _generatorId;

        private readonly long MASK_SEQUENCE;
        private readonly long MASK_TIME;
        private readonly long MASK_GENERATOR;

        private readonly int SHIFT_TIME;
        private readonly int SHIFT_GENERATOR;

        private readonly ITimeSource _timesource;

        // Object to lock() on while generating Id's
        private object genlock = new object();

        /// <summary>
        /// Gets the Id of the generator.
        /// </summary>
        public int Id { get { return _generatorId; } }

        /// <summary>
        /// Gets the epoch for the <see cref="IdGenerator"/>.
        /// </summary>
        public DateTime Epoch { get { return _epoch; } }

        /// <summary>
        /// Gets the <see cref="MaskConfig"/> for the <see cref="IdGenerator"/>.
        /// </summary>
        public MaskConfig MaskConfig { get { return _maskconfig; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="IdGenerator"/> class, 2015-01-01 0:00:00Z is used as default 
        /// epoch and the <see cref="P:IdGen.MaskConfig.Default"/> value is used for the <see cref="MaskConfig"/>. The
        /// <see cref="DefaultTimeSource"/> is used to retrieve timestamp information.
        /// </summary>
        /// <param name="generatorId">The Id of the generator.</param>
        public IdGenerator(int generatorId)
            : this(generatorId, defaultepoch) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="IdGenerator"/> class. The <see cref="P:IdGen.MaskConfig.Default"/> 
        /// value is used for the <see cref="MaskConfig"/>.  The <see cref="DefaultTimeSource"/> is used to retrieve
        /// timestamp information.
        /// </summary>
        /// <param name="generatorId">The Id of the generator.</param>
        /// <param name="epoch">The Epoch of the generator.</param>
        public IdGenerator(int generatorId, DateTime epoch)
            : this(generatorId, epoch, MaskConfig.Default) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="IdGenerator"/> class.  The <see cref="DefaultTimeSource"/> is
        /// used to retrieve timestamp information.
        /// </summary>
        /// <param name="generatorId">The Id of the generator.</param>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        public IdGenerator(int generatorId, DateTime epoch, MaskConfig maskConfig)
            : this(generatorId, epoch, maskConfig, defaulttimesource) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="IdGenerator"/> class.
        /// </summary>
        /// <param name="generatorId">The Id of the generator.</param>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        /// <param name="timeSource">The time-source to use when acquiring time data.</param>
        public IdGenerator(int generatorId, DateTime epoch, MaskConfig maskConfig, ITimeSource timeSource)
        {
            if (maskConfig == null)
                throw new ArgumentNullException("maskConfig");

            if (timeSource == null)
                throw new ArgumentNullException("timeSource");

            if (maskConfig.TotalBits != 63)
                throw new InvalidOperationException("Number of bits used to generate Id's is not equal to 63");

            // Precalculate some values
            MASK_TIME = GetMask(maskConfig.TimestampBits);
            MASK_GENERATOR = GetMask(maskConfig.GeneratorIdBits);
            MASK_SEQUENCE = GetMask(maskConfig.SequenceBits);

            if (generatorId > MASK_GENERATOR)
                throw new ArgumentOutOfRangeException(string.Format("GeneratorId must be between 0 and {0} (inclusive).", MASK_GENERATOR));

            SHIFT_TIME = maskConfig.GeneratorIdBits + maskConfig.SequenceBits;
            SHIFT_GENERATOR = maskConfig.SequenceBits;

            // Store instance specific values
            _maskconfig = maskConfig;
            _timesource = timeSource;
            _epoch = epoch;
            _generatorId = generatorId;
        }

        /// <summary>
        /// Creates a new Id.
        /// </summary>
        /// <returns>Returns an Id based on the <see cref="IdGenerator"/>'s epoch, generatorid and sequence.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long CreateId()
        {
            lock (genlock)
            {
                var timestamp = this.GetTimestamp();
                if (timestamp < _lastgen)
                    throw new InvalidSystemClockException(string.Format("Clock moved backwards. Refusing to generate id for {0} milliseconds", _lastgen - timestamp));

                if (timestamp == _lastgen)
                {
                    if (_sequence <= MASK_SEQUENCE)
                        _sequence++;
                    else
                        throw new SequenceOverflowException("Sequence overflow. Refusing to generate id for rest of millisecond.");
                }
                else
                {
                    _sequence = 0;
                    _lastgen = timestamp;
                }

                unchecked
                {
                    return ((timestamp & MASK_TIME) << SHIFT_TIME)
                        + (_generatorId << SHIFT_GENERATOR)         // GeneratorId is already masked, we only need to shift
                        + _sequence;
                }
            }
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the machine-name.
        /// </summary>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the machine-name</returns>
        public static IdGenerator GetMachineSpecificGenerator()
        {
            return GetMachineSpecificGenerator(defaultepoch);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the machine-name.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the machine-name</returns>
        public static IdGenerator GetMachineSpecificGenerator(DateTime epoch)
        {
            return GetMachineSpecificGenerator(epoch, MaskConfig.Default);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the machine-name.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the machine-name</returns>
        public static IdGenerator GetMachineSpecificGenerator(DateTime epoch, MaskConfig maskConfig)
        {
            return GetMachineSpecificGenerator(epoch, maskConfig, defaulttimesource);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the machine-name.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        /// <param name="timeSource">The time-source to use when acquiring time data.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the machine-name</returns>
        public static IdGenerator GetMachineSpecificGenerator(DateTime epoch, MaskConfig maskConfig, ITimeSource timeSource)
        {
            return new IdGenerator(GetMachineHash() & maskConfig.GeneratorIdBits, epoch, maskConfig, timeSource);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.
        /// </summary>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.</returns>
        public static IdGenerator GetThreadSpecificGenerator()
        {
            return GetThreadSpecificGenerator(defaultepoch);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.</returns>
        public static IdGenerator GetThreadSpecificGenerator(DateTime epoch)
        {
            return GetThreadSpecificGenerator(epoch, MaskConfig.Default);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.</returns>
        public static IdGenerator GetThreadSpecificGenerator(DateTime epoch, MaskConfig maskConfig)
        {
            return GetThreadSpecificGenerator(epoch, maskConfig, defaulttimesource);
        }

        /// <summary>
        /// Returns a new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.
        /// </summary>
        /// <param name="epoch">The Epoch of the generator.</param>
        /// <param name="maskConfig">The <see cref="MaskConfig"/> of the generator.</param>
        /// <param name="timeSource">The time-source to use when acquiring time data.</param>
        /// <returns>A new instance of an <see cref="IdGenerator"/> based on the (managed) thread this method is invoked on.</returns>
        public static IdGenerator GetThreadSpecificGenerator(DateTime epoch, MaskConfig maskConfig, ITimeSource timeSource)
        {
            return new IdGenerator(GetThreadId() & maskConfig.GeneratorIdBits, epoch, maskConfig, timeSource);
        }

        /// <summary>
        /// Gets a unique identifier for the current managed thread.
        /// </summary>
        /// <returns>An integer that represents a unique identifier for this managed thread.</returns>
        private static int GetThreadId()
        {
            return Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Gets a hashcode based on the <see cref="Environment.MachineName"/>.
        /// </summary>
        /// <returns>Returns a hashcode based on the <see cref="Environment.MachineName"/>.</returns>
        private static int GetMachineHash()
        {
            return Environment.MachineName.GetHashCode();
        }

        /// <summary>
        /// Gets the number of milliseconds since the <see cref="IdGenerator"/>'s epoch.
        /// </summary>
        /// <returns>Returns the number of milliseconds since the <see cref="IdGenerator"/>'s epoch.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetTimestamp()
        {
            return (long)(_timesource.GetTime() - _epoch).TotalMilliseconds;
        }

        /// <summary>
        /// Returns a bitmask masking out the desired number of bits; a bitmask of 2 returns 000...000011, a bitmask of
        /// 5 returns 000...011111.
        /// </summary>
        /// <param name="bits">The number of bits to mask.</param>
        /// <returns>Returns the desired bitmask.</returns>
        private static long GetMask(byte bits)
        {
            return (1L << bits) - 1;
        }

        /// <summary>
        /// Returns a 'never ending' stream of Id's.
        /// </summary>
        /// <returns>A 'never ending' stream of Id's.</returns>
        private IEnumerable<long> IdStream()
        {
            while (true)
                yield return this.CreateId();
        }

        /// <summary>
        /// Returns an enumerator that iterates over Id's.
        /// </summary>
        /// <returns>An <see cref="IEnumerator&lt;T&gt;"/> object that can be used to iterate over Id's.</returns>
        public IEnumerator<long> GetEnumerator()
        {
            return this.IdStream().GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates over Id's.
        /// </summary>
        /// <returns>An <see cref="IEnumerator"/> object that can be used to iterate over Id's.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
