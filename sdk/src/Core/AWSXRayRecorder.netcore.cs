﻿//-----------------------------------------------------------------------------
// <copyright file="AWSXRayRecorder.netcore.cs" company="Amazon.com">
//      Copyright 2016 Amazon.com, Inc. or its affiliates. All Rights Reserved.
//
//      Licensed under the Apache License, Version 2.0 (the "License").
//      You may not use this file except in compliance with the License.
//      A copy of the License is located at
//
//      http://aws.amazon.com/apache2.0
//
//      or in the "license" file accompanying this file. This file is distributed
//      on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
//      express or implied. See the License for the specific language governing
//      permissions and limitations under the License.
// </copyright>
//-----------------------------------------------------------------------------
using System;
using Amazon.Runtime.Internal.Util;
using Amazon.XRay.Recorder.Core.Exceptions;
using Amazon.XRay.Recorder.Core.Internal.Emitters;
using Amazon.XRay.Recorder.Core.Internal.Entities;
using Amazon.XRay.Recorder.Core.Internal.Utils;
using Amazon.XRay.Recorder.Core.Sampling;
using Microsoft.Extensions.Configuration;
namespace Amazon.XRay.Recorder.Core
{
    /// <summary>
    /// A collection of methods used to record tracing information for AWS X-Ray.
    /// </summary>
    /// <seealso cref="Amazon.XRay.Recorder.Core.IAWSXRayRecorder" />
    public class AWSXRayRecorder : AWSXRayRecorderImpl
    {
        private static readonly Logger _logger = Logger.GetLogger(typeof(AWSXRayRecorder));
        static AWSXRayRecorder _instance;
        public const String LambdaTaskRootKey = "LAMBDA_TASK_ROOT";
        public const String LambdaTraceHeaderKey = "_X_AMZN_TRACE_ID";
 
        private static String _lambdaVariables;
      
        private  XRayOptions _xRayOptions = new XRayOptions();

        /// <summary>
        /// Initializes a new instance of the <see cref="AWSXRayRecorder" /> class.
        /// with default configuration.
        /// </summary>
        public AWSXRayRecorder() : this(new UdpSegmentEmitter())
        {
            
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AWSXRayRecorder" /> class with <see cref="XRayOptions"/>
        /// </summary>
        /// <param name="options">Instance of <see cref="XRayOptions"/>.</param>
        public AWSXRayRecorder(XRayOptions options) : this(new UdpSegmentEmitter(), options)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AWSXRayRecorder" /> class
        /// with given instance of <see cref="IConfiguration" />.
        /// </summary>
        /// <param name="configuration">Instance of <see cref="IConfiguration"/>.</param>
        public static void InitializeInstance(IConfiguration configuration)
        {
            XRayOptions xRayOptions = XRayConfiguration.GetXRayOptions(configuration);
            Instance = new AWSXRayRecorderBuilder().WithPluginsFromConfig(xRayOptions).Build(xRayOptions);
        }

        /// <summary>
        /// Initializes provided instance of the <see cref="AWSXRayRecorder" /> class with 
        /// the instance of <see cref="IConfiguration" />.
        /// </summary>
        /// <param name="configuration">Instance of <see cref="IConfiguration"/>.</param>
        /// <param name="recorder">Instance of <see cref="AWSXRayRecorder"/>.</param>
        public static void InitializeInstance(IConfiguration configuration, AWSXRayRecorder recorder)
        {
            XRayOptions xRayOptions = XRayConfiguration.GetXRayOptions(configuration);
            recorder.XRayOptions = xRayOptions;
            recorder = new AWSXRayRecorderBuilder().WithPluginsFromConfig(xRayOptions).Build(recorder);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AWSXRayRecorder" /> class
        /// with given instance of <see cref="ISegmentEmitter" />.
        /// </summary>
        /// <param name="emitter">Segment emitter</param>
        internal AWSXRayRecorder(ISegmentEmitter emitter) :base(emitter)
        {
            PopulateContexts();
            SamplingStrategy = new LocalizedSamplingStrategy(XRayOptions.SamplingRuleManifest);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AWSXRayRecorder" /> class
        /// with given instance of <see cref="ISegmentEmitter" /> and instance of <see cref="XRayOptions"/>.
        /// </summary>
        /// <param name="emitter">Instance of <see cref="ISegmentEmitter"/>.</param>
        /// <param name="options">Instance of <see cref="XRayOptions"/>.</param>
        internal AWSXRayRecorder(ISegmentEmitter emitter, XRayOptions options) :base(emitter)
        {
            XRayOptions = options;
            PopulateContexts();
            SamplingStrategy = new LocalizedSamplingStrategy(options.SamplingRuleManifest);
        }

        /// <summary>
        /// Gets the singleton instance of <see cref="AWSXRayRecorder"/> with default configuration.
        /// </summary>
        /// <returns>An instance of <see cref="AWSXRayRecorder"/> class.</returns>
        public static AWSXRayRecorder Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new AWSXRayRecorderBuilder().Build();
                }

                return _instance;
            }
            private set
            {
                _instance = value;
            }
        }

        /// <summary>
        /// Instance of <see cref="XRayOptions"/> class.
        /// </summary>
        public XRayOptions XRayOptions { get => _xRayOptions; set => _xRayOptions = value; }

        /// <summary>
        /// Begin a tracing segment. A new tracing segment will be created and started.
        /// </summary>
        /// <param name="name">The name of the segment</param>
        /// <param name="traceId">Trace id of the segment</param>
        /// <param name="parentId">Unique id of the upstream remote segment or subsegment where the downstream call originated from.</param>
        /// <param name="sampleDecision">Sample decision for the segment from upstream service.</param>
        /// <exception cref="ArgumentNullException">The argument has a null value.</exception>
        public override void BeginSegment(string name, string traceId = null, string parentId = null, SampleDecision sampleDecision = SampleDecision.Sampled)
        {
            Segment newSegment;

            if (IsLambda())
            {
                throw new UnsupportedOperationException("Cannot override Facade Segment. New segment not created.");
            }
            else
            {
                 newSegment = new Segment(name, traceId, parentId);
            }

            if (!IsTracingDisabled())
            {
                newSegment.SetStartTimeToNow();
                PopulateNewSegmentAttributes(newSegment);
            }

            newSegment.Sampled = sampleDecision;

            TraceContext.SetEntity(newSegment);
        }

        /// <summary>
        /// End a tracing segment. If all operations of the segments are finished, the segment will be emitted.
        /// </summary>
        /// <exception cref="EntityNotAvailableException">Entity is not available in trace context.</exception>
        public override void EndSegment()
        {
            if (IsLambda())
            {
                throw new UnsupportedOperationException("Cannot override Facade Segment. New segment not created.");
            }

            try
            {
                // If the request is not sampled, a segment will still be available in TraceContext.
                // Need to clean up the segment, but do not emit it.
                Segment segment = (Segment)TraceContext.GetEntity();
                if (!IsTracingDisabled())
                {
                    segment.SetEndTimeToNow();
                    ProcessEndSegment(segment);
                }

                TraceContext.ClearEntity();
            }
            catch (EntityNotAvailableException e)
            {
                HandleEntityNotAvailableException(e, "Failed to end segment because cannot get the segment from trace context.");
            }
            catch (InvalidCastException e)
            {
                HandleEntityNotAvailableException(new EntityNotAvailableException("Failed to cast the entity to Segment.", e), "Failed to cast the entity to Segment.");
            }
        }

        /// <summary>
        /// Begin a tracing subsegment. A new segment will be created and added as a subsegment to previous segment/subsegment.
        /// </summary>
        /// <param name="name">Name of the operation</param>
        /// <exception cref="ArgumentNullException">The argument has a null value.</exception>
        /// <exception cref="EntityNotAvailableException">Entity is not available in trace context.</exception>
        public override void BeginSubsegment(string name)
        {
            try
            {
                if (IsTracingDisabled())
                {
                    _logger.DebugFormat("X-Ray tracing is disabled, do not start subsegment");
                    return;
                }

                if (IsLambda())
                {
                    ProcessSubsegmentInLambdaContext(name);
                }
                else
                {
                    AddSubsegment(new Subsegment(name));
                }
            }
            catch (EntityNotAvailableException e)
            {
                HandleEntityNotAvailableException(e, "Failed to start subsegment because the parent segment is not available.");
            }
        }

        /// <summary>
        /// Begin a tracing subsegment. A new subsegment will be created and added as a subsegment to previous facade segment or subsegment.
        /// </summary>
        private void ProcessSubsegmentInLambdaContext(string name)
        {
            if (!TraceContext.IsEntityPresent()) //No facade segment available and first subsegment of a subsegment branch needs to be added
            {
                AddFacadeSegment(name);
                AddSubsegmentInLambdaContext(new Subsegment(name));
            }
            else //continuation of subsegment branch
            {
                var parentSubsegment = (Subsegment)TraceContext.GetEntity();
                var environmentRootTraceId = TraceHeader.FromString(GetTraceVariablesFromEnvironment()).RootTraceId;
                if ((null != environmentRootTraceId) && !environmentRootTraceId.Equals(parentSubsegment.RootSegment.TraceId)) //If true, customer has leaked subsegments across invocation
                {
                    TraceContext.ClearEntity(); //reset TraceContext
                    BeginSubsegment(name); //This adds Facade segment with updated environment variables
                }
                else
                {
                    AddSubsegmentInLambdaContext(new Subsegment(name));
                }

            }
        }

        /// <summary>
        /// Begin a Facade Segment.
        /// </summary>
        internal void AddFacadeSegment(String name = null)
        {
            _lambdaVariables = GetTraceVariablesFromEnvironment();
            _logger.DebugFormat("Lambda Environment detected. Lambda variables: {0}", _lambdaVariables);
            TraceHeader traceHeader;

            if (!TraceHeader.TryParseAll(_lambdaVariables, out traceHeader))
            {
                if (name != null)
                {
                    _logger.DebugFormat("Lambda variables are missing/not valid trace Id, parent id or sampling decision = {0}, discarding subsegment = {1}", _lambdaVariables,name);
                }
                else
                {
                    _logger.DebugFormat("Lambda variables are missing/not valid trace Id, parent id or sampling decision = {0}, discarding subsegment", _lambdaVariables);
                }

                traceHeader = new TraceHeader();
                traceHeader.RootTraceId = TraceId.NewId();
                traceHeader.ParentId = null;
                traceHeader.Sampled = SampleDecision.NotSampled;
            }

            Segment newSegment = new FacadeSegment("Facade", traceHeader.RootTraceId, traceHeader.ParentId);
            newSegment.Sampled = traceHeader.Sampled;
            TraceContext.SetEntity(newSegment);
        }

        private void AddSubsegmentInLambdaContext(Subsegment subsegment)
        {
            // If the request is not sampled, the passed subsegment will still be available in TraceContext to
            // stores the information of the trace. The trace information will still propagated to 
            // downstream service, in case downstream may overwrite the sample decision.
            Entity parentEntity = TraceContext.GetEntity();
            parentEntity.AddSubsegment(subsegment);
            subsegment.Sampled = parentEntity.Sampled;
            subsegment.SetStartTimeToNow();
            TraceContext.SetEntity(subsegment);
        }

        private void AddSubsegment(Subsegment subsegment)
        {
            // If the request is not sampled, a segment will still be available in TraceContext to
            // stores the information of the trace. The trace information will still propagated to 
            // downstream service, in case downstream may overwrite the sample decision.
            Entity parentEntity = TraceContext.GetEntity();

            // If the segment is not sampled, do nothing and exit.
            if (parentEntity.Sampled != SampleDecision.Sampled)
            {
                _logger.DebugFormat("Do not start subsegment because the segment doesn't get sampled. ({0})", subsegment.Name);
                return;
            }

            parentEntity.AddSubsegment(subsegment);
            subsegment.Sampled = parentEntity.Sampled;
            subsegment.SetStartTimeToNow();
            TraceContext.SetEntity(subsegment);
        }
       
        /// <summary>
        /// End a subsegment.
        /// </summary>
        /// <exception cref="EntityNotAvailableException">Entity is not available in trace context.</exception>
        public override void EndSubsegment()
        {
            try
            {
                if (IsTracingDisabled())
                {
                    _logger.DebugFormat("X-Ray tracing is disabled, do not end subsegment");
                    return;
                }

                if (IsLambda())
                {
                    ProcessEndSubsegmentInLambdaContext();
                }
                else
                {
                    ProcessEndSubsegment();
                }
            }
            catch (EntityNotAvailableException e)
            {
                HandleEntityNotAvailableException(e, "Failed to end subsegment because subsegment is not available in trace context.");
            }
            catch (InvalidCastException e)
            {
                HandleEntityNotAvailableException(new EntityNotAvailableException("Failed to cast the entity to Subsegment.", e), "Failed to cast the entity to Subsegment.");
            }
        }

        private void ProcessEndSubsegmentInLambdaContext()
        {
            var subsegment = PrepEndSubsegmentInLambdaContext();
            
            // Check emittable
            if (subsegment.IsEmittable())
            {
                // Emit
                Emitter.Send(subsegment.RootSegment);
            }
            else if (ShouldStreamSubsegments(subsegment))
            {
                StreamSubsegments(subsegment.RootSegment);
            }

            if (TraceContext.IsEntityPresent() && TraceContext.GetEntity().GetType() == typeof(FacadeSegment)) //implies FacadeSegment in the Trace Context
            {
                EndFacadeSegment();
                return;
            }
        }

        private Subsegment PrepEndSubsegmentInLambdaContext()
        {
            // If the request is not sampled, a subsegment will still be available in TraceContext.
            //This behavor is specific to AWS Lambda environment
            Entity entity = TraceContext.GetEntity();

            Subsegment subsegment = (Subsegment)entity;

            // Set end time
            subsegment.SetEndTimeToNow();
            subsegment.IsInProgress = false;

            // Restore parent segment to trace context
            if (subsegment.Parent != null)
            {
                TraceContext.SetEntity(subsegment.Parent);
            }

            // Drop ref count
            subsegment.Release();

            return subsegment;
        }

        private void EndFacadeSegment()
        {
            try
            {
                // If the request is not sampled, a segment will still be available in TraceContext.
                // Need to clean up the segment, but do not emit it.
                FacadeSegment facadeSegment = (FacadeSegment)TraceContext.GetEntity();

                if (!IsTracingDisabled())
                {
                    PrepEndSegment(facadeSegment);
                    if (facadeSegment.Sampled == SampleDecision.Sampled && facadeSegment.RootSegment != null && facadeSegment.RootSegment.Size >= 0)
                    {
                        StreamSubsegments(facadeSegment); //Facade segment is not emitted, all its subsegments, if emmittable, are emitted
                    }
                }

                TraceContext.ClearEntity();
            }
            catch (EntityNotAvailableException e)
            {
                HandleEntityNotAvailableException(e, "Failed to end facade segment because cannot get the segment from trace context.");
            }
            catch (InvalidCastException e)
            {
                HandleEntityNotAvailableException(new EntityNotAvailableException("Failed to cast the entity to Facade segment.", e), "Failed to cast the entity to Facade Segment.");
            }
        }

        /// <summary>
        /// Checks whether current execution is in AWS Lambda.
        /// </summary>
        /// <returns>Returns true if current execution is in AWS Lambda.</returns>
        public Boolean IsLambda()
        {
           var lambdaTaskRootKey = Environment.GetEnvironmentVariable(LambdaTaskRootKey);
           if (!Object.Equals(lambdaTaskRootKey, null))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns value set for environment variable "_X_AMZN_TRACE_ID"
        /// </summary>
        private String GetTraceVariablesFromEnvironment()
        {
            var lambdaTraceHeader = Environment.GetEnvironmentVariable(LambdaTraceHeaderKey);
            return lambdaTraceHeader;
        }

        /// <summary>
        /// Checks whether Tracing is enabled or disabled.
        /// </summary>
        /// <returns> Returns true if Tracing is disabled else false.</returns>
        public override bool IsTracingDisabled()
        {
           return XRayOptions.IsXRayTracingDisabled;
        }

        /// <summary>
        ///  Configures Logger to <see cref="Amazon.LoggingOptions"/>.
        /// </summary>
        /// <param name="loggingOptions">Enum of <see cref="Amazon.LoggingOptions"/>.</param>
        public static void RegisterLogger(Amazon.LoggingOptions loggingOptions)
        {
            AWSConfigs.LoggingConfig.LogTo = loggingOptions;
        }
    }
}
