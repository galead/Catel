// --------------------------------------------------------------------------------------------------------------------
// <copyright file="WeakEventListener.cs" company="Catel development team">
//   Copyright (c) 2008 - 2014 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel
{
    using System;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Reflection;
    
    using Catel.Caching;
    using Logging;
    using Reflection;

#if NETFX_CORE || PCL
    using EventArgsBase = System.Object;
    using System.Runtime.InteropServices.WindowsRuntime;
#else
    using EventArgsBase = System.EventArgs;
#endif

    /// <summary>
    /// Implements a weak event listener that allows the owner to be garbage
    /// collected if its only remaining link is an event handler.
    /// </summary>
    /// <typeparam name="TSource">Type of source for the event.</typeparam>
    /// <typeparam name="TTarget">Type of target listening for the event.</typeparam>
    /// <typeparam name="TEventArgs">Type of event arguments for the event.</typeparam>
    /// <example>
    /// Initially, the code must be used in this way: 
    /// <para />
    /// <code>
    ///  <![CDATA[
    ///     var source = new EventSource();
    ///     var listener = new EventListener();
    ///
    ///     WeakEventListener<EventListener, EventSource, EventArgs>.SubscribeToWeakEvent(listener, source, "Event", listener.OnEvent);
    /// ]]>
    /// </code>
    /// </example>
    public class WeakEventListener<TTarget, TSource, TEventArgs> : IWeakEventListener
        where TTarget : class
        where TSource : class
#if !NETFX_CORE && !PCL
        where TEventArgs : EventArgsBase
#endif
    {
        /// <summary>
        /// Open instance delegate which allows the creation of an instance method without an actual reference
        /// to the target.
        /// </summary>
        public delegate void OpenInstanceHandler(TTarget @this, object sender, TEventArgs e);

        #region Fields
        /// <summary>
        /// The log.
        /// </summary>
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// WeakReference to the target listening for the event.
        /// </summary>
        private readonly WeakReference _weakTarget;

        /// <summary>
        /// To hold a reference to source object. With this instance the WeakEventListener 
        /// can guarantee that the handler get unregistered when listener is released.
        /// </summary>
        private readonly WeakReference _weakSource;

        /// <summary>
        /// The event name this listener is automatically subscribed to. If this value is <c>null</c>, the
        /// listener is not automatically registered to any event.
        /// </summary>
        private string _automaticallySubscribedEventName;

        /// <summary>
        /// Delegate that needs to be unsubscribed when registered automatically.
        /// </summary>
        private Delegate _internalEventDelegate;

        /// <summary>
        /// The type for event subscriptions. This can differ from TSource for explicitly implemented events.
        /// </summary>
        private readonly Type _typeForEventSubscriptions;

#if NETFX_CORE || PCL
        /// <summary>
        /// The event registration token that is required to remove the event handler in WinRT.
        /// </summary>
        private EventRegistrationToken _eventRegistrationToken;
#endif
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instances of the WeakEventListener class.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="typeForEventSubscriptions">The type for event subscriptions.</param>
        private WeakEventListener(TTarget target, TSource source, Type typeForEventSubscriptions)
        {
            Argument.IsNotNull("typeForEventSubscriptions", typeForEventSubscriptions);

            _typeForEventSubscriptions = typeForEventSubscriptions;

            IsStaticEventHandler = (target == null);
            if (target != null)
            {
                _weakTarget = new WeakReference(target);
            }

            IsStaticEvent = (source == null);
            if (source != null)
            {
                _weakSource = new WeakReference(source);
            }
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the method to call when the event fires.
        /// </summary>
        internal OpenInstanceHandler OnEventAction { get; set; }

        /// <summary>
        /// Gets or sets the method to call when the static event fires.
        /// </summary>
        /// <value>The on static event action.</value>
        internal EventHandler<TEventArgs> OnStaticEventAction { get; set; }

        /// <summary>
        /// Gets the target or <c>null</c> if there is no target.
        /// </summary>
        /// <value>The target.</value>
        public object Target { get { return (_weakTarget != null) ? _weakTarget.Target : null; } }

        /// <summary>
        /// Gets the source or <c>null</c> if there is no source.
        /// </summary>
        /// <value>The target.</value>
        public object Source { get { return (_weakSource != null) ? _weakSource.Target : null; } }

        /// <summary>
        /// Gets the type of the target.
        /// </summary>
        /// <value>The type of the target.</value>
        public Type TargetType { get { return typeof(TTarget); } }

        /// <summary>
        /// Gets the type of the source.
        /// </summary>
        /// <value>The type of the source.</value>
        public Type SourceType { get { return typeof(TSource); } }

        /// <summary>
        /// Gets the type of the event args.
        /// </summary>
        /// <value>The type of the event args.</value>
        public Type EventArgsType { get { return typeof(TEventArgs); } }

        /// <summary>
        /// Gets a value indicating whether the event source has not yet been garbage collected.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the event source has not yet been garbage collected; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// In case of static events, this property always returns <c>false</c>.
        /// </remarks>
        public bool IsSourceAlive { get { return (_weakSource != null) && _weakSource.IsAlive; } }

        /// <summary>
        /// Gets a value indicating whether the event target has not yet been garbage collected.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the event target has not yet been garbage collected; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// In case of static event handlers, this property always returns <c>false</c>.
        /// </remarks>
        public bool IsTargetAlive { get { return (_weakTarget != null) && _weakTarget.IsAlive; } }

        /// <summary>
        /// Gets a value indicating whether this instance represents a static event.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance represents a static event; otherwise, <c>false</c>.
        /// </value>
        public bool IsStaticEvent { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance represents a static event handler.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance represents a static event handler; otherwise, <c>false</c>.
        /// </value>
        public bool IsStaticEventHandler { get; private set; }
        #endregion

        #region Methods
        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakGenericEvent(TTarget target, TSource source, string eventName, EventHandler<TEventArgs> handler)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);
            Argument.IsNotNull("handler", handler);

            return SubscribeToWeakEventWithExplicitSourceType<TSource>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="PropertyChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakPropertyChangedEvent(TTarget target, TSource source, PropertyChangedEventHandler handler, string eventName = "PropertyChanged")
        {
            Argument.IsNotNull("handler", handler);
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            return SubscribeToWeakEventWithExplicitSourceType<INotifyPropertyChanged>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="NotifyCollectionChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakCollectionChangedEvent(TTarget target, TSource source, NotifyCollectionChangedEventHandler handler, string eventName = "CollectionChanged")
        {
            return SubscribeToWeakEventWithExplicitSourceType<INotifyCollectionChanged>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakEvent(TTarget target, TSource source, string eventName, Delegate handler)
        {
            return SubscribeToWeakEventWithExplicitSourceType<TSource>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <typeparam name="TExplicitSourceType">The final source type, which must be specified for explicitly implemented events.</typeparam>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakEventWithExplicitSourceType<TExplicitSourceType>(TTarget target, TSource source, string eventName, Delegate handler)
            where TExplicitSourceType : class
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);
            Argument.IsNotNull("handler", handler);

            if ((source == null) && (target == null))
            {
                const string error = "Both the source and target are null, which means that a static event handler subscribes to a static event. In such cases, there are no memory leaks, so there is no reason to use this class";

                Log.Error(error);

                throw new InvalidOperationException(error);
            }

            var methodInfo = handler.GetMethodInfoEx();
            if ((methodInfo.Name.Contains("_AnonymousDelegate>")) || (methodInfo.DeclaringType.FullName.Contains("__DisplayClass")))
            {
                const string error = "Anonymous delegates are not supported because they are located in a private class";
                Log.Error(error);
                throw new NotSupportedException(error);
            }

            var weakListener = new WeakEventListener<TTarget, TSource, TEventArgs>(target, source, typeof(TExplicitSourceType));

            try
            {
                weakListener.SubscribeToEvent(source, eventName);
            }
            catch (ArgumentException ex)
            {
                const string error = "Failed to create the delegate. Probably the wrong type of EventArgs is used and does not match the EventHandler<TEventArgs>";

                Log.Error(error);

                throw new InvalidOperationException(error, ex);
            }

            if (weakListener.IsStaticEventHandler)
            {
                var del = DelegateHelper.CreateDelegate(typeof(EventHandler<TEventArgs>), methodInfo);
                weakListener.OnStaticEventAction = (EventHandler<TEventArgs>)del;
            }
            else
            {
                var del = DelegateHelper.CreateDelegate(typeof(OpenInstanceHandler), methodInfo);
                weakListener.OnEventAction = (OpenInstanceHandler)del;
            }

            return weakListener;
        }

        /// <summary>
        /// Subscribes to the specific event. If the event occurs, the <see cref="OnEvent" /> method will be invoked.
        /// </summary>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <exception cref="ArgumentException">The <paramref name="eventName" /> is <c>null</c> or whitespace.</exception>
        private void SubscribeToEvent(object source, string eventName)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            // Try normal event first            
            if (SubscribeToEventUsingNormalImplementation(source, eventName))
            {
                _automaticallySubscribedEventName = eventName;
                return;
            }

            string error = string.Format("No add-method available for event '{0}', cannot subscribe using weak events. Make sure the event is public", eventName);
            Log.Error(error);
            throw new NotSupportedException(error);
        }

        /// <summary>
        /// Subscribes to the specific event using normal implementations. If the event occurs, the
        /// <see cref="OnEvent" /> method will be invoked.
        /// </summary>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns><c>true</c> if subscribed successfully, <c>false</c> otherwise.</returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName" /> is <c>null</c> or whitespace.</exception>
        private bool SubscribeToEventUsingNormalImplementation(object source, string eventName)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            var eventInfo = _typeForEventSubscriptions.GetEventEx(eventName, true, true);
            if (eventInfo == null)
            {
                return false;
            }

#if NETFX_CORE || PCL
            var addMethod = eventInfo.AddMethod;
#else
            var addMethod = eventInfo.GetAddMethod();
#endif

            if (addMethod == null)
            {
                return false;
            }

            return SubscribeToEventUsingMethodInfo(source, addMethod);
        }

        /// <summary>
        /// Subscribes to the event using method.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="methodInfo">The method info.</param>
        /// <returns><c>true</c> if subscribed successfully, <c>false</c> otherwise.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="methodInfo" /> is <c>null</c>.</exception>
        private bool SubscribeToEventUsingMethodInfo(object source, MethodInfo methodInfo)
        {
            Argument.IsNotNull("methodInfo", methodInfo);

            //var handlerType = eventInfo.EventHandlerType;
            var handlerType = methodInfo.GetParameters()[0].ParameterType;

            _internalEventDelegate = DelegateHelper.CreateDelegate(handlerType, this, "OnEvent");

#if NETFX_CORE || PCL
            if (methodInfo.ReturnType == typeof(void))
            {
                methodInfo.Invoke(source, new object[] { _internalEventDelegate });
            }
            else
            {
                _eventRegistrationToken = (EventRegistrationToken)methodInfo.Invoke(source, new object[] { _internalEventDelegate });
            }
#else
            methodInfo.Invoke(source, new object[] { _internalEventDelegate });
#endif
            return true;
        }

        /// <summary>
        /// Unsubscribes from the specific event. If the event occurs, the <see cref="OnEvent"/> method will no longer be invoked.
        /// </summary>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        private void UnsubscribeFromEvent(object source, string eventName)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            if (_internalEventDelegate == null)
            {
                return;
            }

            try
            {
                // First, try regular events
                if (UnsubscribeFromEventUsingNormalImplementation(source, eventName))
                {
                    return;
                }

#if NET
                // Second, try explicit interface implementations
                if (UnsubscribeFromEventUsingExplicitInterfaceImplementation(source, eventName))
                {
                    return;
                }
#endif

                Log.Warning("Failed to unsubscribe from event '{0}'", eventName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to unsubscribe from event '{0}'", eventName);
            }
        }

        /// <summary>
        /// Unsubscribes from the specific event using normal implementations.
        /// </summary>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns><c>true</c> if subscribed successfully, <c>false</c> otherwise.</returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName" /> is <c>null</c> or whitespace.</exception>
        private bool UnsubscribeFromEventUsingNormalImplementation(object source, string eventName)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            var eventInfo = _typeForEventSubscriptions.GetEventEx(eventName, true, true);
            if (eventInfo == null)
            {
                return false;
            }

#if NETFX_CORE || PCL
            var removeMethod = eventInfo.RemoveMethod;
#else
            var removeMethod = eventInfo.GetRemoveMethod();
#endif

            if (removeMethod == null)
            {
                return false;
            }

            return UnsubscribeFromEventUsingMethodInfo(source, removeMethod);
        }

        /// <summary>
        /// Unsubscribes from the specific event using explicit interface implementations.
        /// </summary>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns><c>true</c> if subscribed successfully, <c>false</c> otherwise.</returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName" /> is <c>null</c> or whitespace.</exception>
        private bool UnsubscribeFromEventUsingExplicitInterfaceImplementation(object source, string eventName)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);

            var expectedMethodName = string.Format(".remove_{0}", eventName);
            var methods = (from method in _typeForEventSubscriptions.GetMethodsEx(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                           where method.Name.Contains(expectedMethodName)
                           select method);

            foreach (var method in methods)
            {
                if (UnsubscribeFromEventUsingMethodInfo(source, method))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Unsubscribes from the event using method.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="methodInfo">The method info.</param>
        /// <returns><c>true</c> if subscribed successfully, <c>false</c> otherwise.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="methodInfo" /> is <c>null</c>.</exception>
        private bool UnsubscribeFromEventUsingMethodInfo(object source, MethodInfo methodInfo)
        {
            Argument.IsNotNull("methodInfo", methodInfo);

#if WIN80
            methodInfo.Invoke(source, new object[] { _eventRegistrationToken });
#else
            methodInfo.Invoke(source, new object[] { _internalEventDelegate });
#endif

            return true;
        }

        /// <summary>
        /// Handler for the subscribed event calls OnEventAction to handle it.
        /// </summary>
        /// <param name="source">Event source.</param>
        /// <param name="eventArgs">Event arguments.</param>
        // ReSharper disable UnusedMember.Global
        public void OnEvent(object source, TEventArgs eventArgs)
        // ReSharper restore UnusedMember.Global
        {
            if (!IsStaticEventHandler && (Target == null))
            {
                Detach();
                return;
            }

            var target = (TTarget)Target;

            var onEventAction = OnEventAction;
            if (onEventAction != null)
            {
                onEventAction(target, source, eventArgs);
            }

            var onStaticEventAction = OnStaticEventAction;
            if (onStaticEventAction != null)
            {
                onStaticEventAction(source, eventArgs);
            }
        }

        /// <summary>
        /// Detaches from the subscribed event.
        /// </summary>
        public void Detach()
        {
            if (!IsStaticEvent && (Source == null))
            {
                Log.Warning("Event on source '{0}' is not static, yet the source does no longer exists", typeof(TSource).FullName);
                return;
            }

            if (_automaticallySubscribedEventName != null)
            {
                UnsubscribeFromEvent(Source, _automaticallySubscribedEventName);
            }
        }
        #endregion
    }

    /// <summary>
    /// Convenience implementation of the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> that automatically determines the type
    /// of the event args.
    /// </summary>
    /// <typeparam name="TTarget">The type of the target.</typeparam>
    /// <typeparam name="TSource">The type of the source.</typeparam>
    public static class WeakEventListener<TTarget, TSource>
        where TTarget : class
        where TSource : class
    {
        /// <summary>
        /// The log.
        /// </summary>
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The event handler event arguments cache.
        /// </summary>
        private static readonly ICacheStorage<Type, Type> _eventHandlerEventArgsCache = new CacheStorage<Type, Type>();

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <typeparam name="TEventArgs">The type of the event args.</typeparam>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>The created event listener.</returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>,
        /// <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakGenericEvent<TEventArgs>(TTarget target, TSource source, string eventName, EventHandler<TEventArgs> handler)
#if !NETFX_CORE && !PCL
 where TEventArgs : EventArgsBase
#endif
        {
            return SubscribeToWeakEvent(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="PropertyChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakPropertyChangedEvent(TTarget target, TSource source, PropertyChangedEventHandler handler, string eventName = "PropertyChanged")
        {
            return SubscribeToWeakEventWithExplicitSourceType<INotifyPropertyChanged>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="NotifyCollectionChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// /// <param name="eventName">Name of the event.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakCollectionChangedEvent(TTarget target, TSource source, NotifyCollectionChangedEventHandler handler, string eventName = "CollectionChanged")
        {
            return SubscribeToWeakEventWithExplicitSourceType<INotifyCollectionChanged>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>, 
        /// <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakEvent(TTarget target, TSource source, string eventName, Delegate handler)
        {
            return SubscribeToWeakEventWithExplicitSourceType<TSource>(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <typeparam name="TExplicitSourceType">The final source type, which must be specified for explicitly implemented events.</typeparam>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <remarks>
        /// This method can only be used for non-static event sources and targets. If static events or listeners are required, use
        /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>, <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakEventWithExplicitSourceType<TExplicitSourceType>(TTarget target, TSource source, string eventName, Delegate handler)
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);
            Argument.IsNotNull("handler", handler);

            var handlerType = handler.GetType();
            var eventArgsType = _eventHandlerEventArgsCache.GetFromCacheOrFetch(handlerType, () =>
            {
                Type type = null;

                if (handlerType == typeof(PropertyChangedEventHandler))
                {
                    type = typeof(PropertyChangedEventArgs);
                }
                else if (handlerType == typeof(NotifyCollectionChangedEventHandler))
                {
                    type = typeof(NotifyCollectionChangedEventArgs);
                }
                else if (handlerType.IsGenericTypeEx())
                {
                    type = handlerType.GetGenericArgumentsEx()[0];
                    if (!typeof(EventArgsBase).IsAssignableFromEx(type))
                    {
                        type = null;
                    }
                }

                return type;
            });

            if (eventArgsType == null)
            {
                string error = string.Format("Only handlers of type 'PropertyChangedEventHandler', 'NotifyCollectionChangedEventHandler' or 'EventHandler<TEventArgs' are supported. '{0}' does not belong to these supported types", handler.GetType().Name);
                Log.Error(error);

                throw new NotSupportedException(error);
            }

            var listenerType = typeof(WeakEventListener<TTarget, TSource, EventArgsBase>).GetGenericTypeDefinition().MakeGenericType(new[] { typeof(TTarget), typeof(TSource), eventArgsType });

            var methodInfo = listenerType.GetMethodEx("SubscribeToWeakEventWithExplicitSourceType", new[] { typeof(TTarget), typeof(TSource), typeof(string), typeof(Delegate) }, BindingFlagsHelper.GetFinalBindingFlags(true, true));
            //var methodInfo = listenerType.GetMethodEx("SubscribeToWeakEvent", new[] { typeof(TTarget), typeof(TSource), typeof(string), handlerType }, BindingFlagsHelper.GetFinalBindingFlags(true, true));
            if (methodInfo == null)
            {
                const string error = "Expected to find the SubscribeToWeakEventWithExplicitSourceType on WeakEventListener<TTarget, TSource, TEventArgs>, but did not find it";
                Log.Error(error);

                throw new InvalidOperationException(error);
            }

            var genericMethodInfo = methodInfo.MakeGenericMethod(typeof(TExplicitSourceType));
            return (IWeakEventListener)genericMethodInfo.Invoke(null, new object[] { target, source, eventName, handler });
        }
    }

    /// <summary>
    /// Convenience implementation of the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> that automatically determines the type
    /// of the event source, the event target and the event args.
    /// </summary>
    /// <remarks>
    /// This class can only be used for non-static event sources and targets. If static events or listeners are required, use
    /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
    /// </remarks>
    public static class WeakEventListener
    {
        /// <summary>
        /// The log.
        /// </summary>
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <typeparam name="TEventArgs">The type of the event args.</typeparam>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>The created event listener.</returns>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>,
        /// <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakGenericEvent<TEventArgs>(this object target, object source, string eventName, EventHandler<TEventArgs> handler)
#if !NETFX_CORE && !PCL
 where TEventArgs : EventArgsBase
#endif
        {
            Argument.IsNotNullOrWhitespace("eventName", eventName);
            Argument.IsNotNull("handler", handler);

            return SubscribeToWeakEvent(target, source, eventName, handler);
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="PropertyChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <remarks>
        /// This method can only be used for non-static event sources and targets. If static events or listeners are required, use
        /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakPropertyChangedEvent(this object target, object source, PropertyChangedEventHandler handler, string eventName = "PropertyChanged")
        {
            return SubscribeToWeakEvent(target, source, eventName, handler, typeof(INotifyPropertyChanged));
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// <para />
        /// This method subscribes to the <see cref="NotifyCollectionChangedEventHandler"/> which does not follow the <c>EventHandler{TEventArgs}</c> convention.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <remarks>
        /// This method can only be used for non-static event sources and targets. If static events or listeners are required, use
        /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakCollectionChangedEvent(this object target, object source, NotifyCollectionChangedEventHandler handler, string eventName = "CollectionChanged")
        {
            return SubscribeToWeakEvent(target, source, eventName, handler, typeof(INotifyCollectionChanged));
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <remarks>
        /// This method can only be used for non-static event sources and targets. If static events or listeners are required, use
        /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>, <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        public static IWeakEventListener SubscribeToWeakEvent(this object target, object source, string eventName, Delegate handler)
        {
            Argument.IsNotNull("source", source);

            return SubscribeToWeakEvent(target, source, eventName, handler, source.GetType());
        }

        /// <summary>
        /// Subscribes to a weak event by using one single method. This method also takes care of automatic
        /// unsubscription of the event.
        /// </summary>
        /// <param name="target">Instance subscribing to the event, should be <c>null</c> for static event handlers.</param>
        /// <param name="source">The source of the event, should be <c>null</c> for static events.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The handler to execute when the event occurs.</param>
        /// <param name="eventSourceType">The event source to use for type implementations. Differs from the source type for explicitly implemented events.</param>
        /// <returns>
        /// The created event listener.
        /// </returns>
        /// <remarks>
        /// This method can only be used for non-static event sources and targets. If static events or listeners are required, use
        /// the <see cref="WeakEventListener{TTarget,TSource,TEventArgs}"/> class.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="source"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="eventName"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="source"/> and <paramref name="target"/> are both <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="eventName"/> does not exist or not accessible.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is not of type <see cref="PropertyChangedEventHandler"/>, <see cref="NotifyCollectionChangedEventHandler"/> or <see cref="EventHandler{TEventArgs}"/>.</exception>
        /// <exception cref="NotSupportedException">The <paramref name="handler"/> is an anonymous delegate.</exception>
        private static IWeakEventListener SubscribeToWeakEvent(object target, object source, string eventName, Delegate handler, Type eventSourceType)
        {
            Argument.IsNotNull("target", target);
            Argument.IsNotNull("source", source);
            Argument.IsNotNullOrWhitespace("eventName", eventName);
            Argument.IsNotNull("handler", handler);

            var targetType = target.GetType();
            var sourceType = source.GetType();

            var listenerType = typeof(WeakEventListener<object, object>).GetGenericTypeDefinition().MakeGenericType(new[] { targetType, sourceType });

            var methodInfo = listenerType.GetMethodEx("SubscribeToWeakEventWithExplicitSourceType", new[] { targetType, sourceType, typeof(string), typeof(Delegate) }, BindingFlagsHelper.GetFinalBindingFlags(true, true));
            if (methodInfo == null)
            {
                const string error = "Expected to find the SubscribeToWeakEventWithExplicitSourceType on WeakEventListener<TTarget, TSource>, but did not find it";
                Log.Error(error);

                throw new InvalidOperationException(error);
            }

            var genericMethodInfo = methodInfo.MakeGenericMethod(eventSourceType);
            return (IWeakEventListener)genericMethodInfo.Invoke(null, new[] { target, source, eventName, handler });
        }
    }
}