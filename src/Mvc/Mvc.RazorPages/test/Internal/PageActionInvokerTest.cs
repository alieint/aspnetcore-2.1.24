// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Internal;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.RazorPages.Internal
{
    public class PageActionInvokerTest : CommonResourceInvokerTest
    {
        #region Diagnostics

        [Fact]
        public async Task Invoke_WritesDiagnostic_ActionSelected()
        {
            // Arrange
            var actionDescriptor = CreateDescriptorForSimplePage();
            var displayName = actionDescriptor.DisplayName;

            var routeData = new RouteData();
            routeData.Values.Add("tag", "value");

            var listener = new TestDiagnosticListener();

            var invoker = CreateInvoker(filters: null, actionDescriptor: actionDescriptor, listener: listener, routeData: routeData);

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.NotNull(listener.BeforeAction?.ActionDescriptor);
            Assert.NotNull(listener.BeforeAction?.HttpContext);

            var routeValues = listener.BeforeAction?.RouteData?.Values;
            Assert.NotNull(routeValues);

            Assert.Equal(1, routeValues.Count);
            Assert.Contains(routeValues, kvp => kvp.Key == "tag" && string.Equals(kvp.Value, "value"));
        }

        [Fact]
        public async Task Invoke_WritesDiagnostic_ActionInvoked()
        {
            // Arrange
            var actionDescriptor = CreateDescriptorForSimplePage();
            var displayName = actionDescriptor.DisplayName;

            var routeData = new RouteData();
            routeData.Values.Add("tag", "value");

            var listener = new TestDiagnosticListener();

            var invoker = CreateInvoker(filters: null, actionDescriptor: actionDescriptor, listener: listener, routeData: routeData);

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.NotNull(listener.AfterAction?.ActionDescriptor);
            Assert.NotNull(listener.AfterAction?.HttpContext);
        }

        #endregion

        #region Page Context

        [Fact]
        public async Task AddingValueProviderFactory_AtResourceFilter_IsAvailableInPageContext()
        {
            // Arrange
            var valueProviderFactory2 = Mock.Of<IValueProviderFactory>();
            var resourceFilter = new Mock<IResourceFilter>();
            resourceFilter
                .Setup(f => f.OnResourceExecuting(It.IsAny<ResourceExecutingContext>()))
                .Callback<ResourceExecutingContext>((resourceExecutingContext) =>
                {
                    resourceExecutingContext.ValueProviderFactories.Add(valueProviderFactory2);
                });
            var valueProviderFactory1 = Mock.Of<IValueProviderFactory>();
            var valueProviderFactories = new List<IValueProviderFactory>
            {
                valueProviderFactory1
            };

            var invoker = CreateInvoker(
                new IFilterMetadata[] { resourceFilter.Object }, valueProviderFactories: valueProviderFactories);

            // Act
            await invoker.InvokeAsync();

            // Assert
            var pageContext = Assert.IsType<PageActionInvoker>(invoker).PageContext;
            Assert.NotNull(pageContext);
            Assert.Equal(2, pageContext.ValueProviderFactories.Count);
            Assert.Same(valueProviderFactory1, pageContext.ValueProviderFactories[0]);
            Assert.Same(valueProviderFactory2, pageContext.ValueProviderFactories[1]);
        }

        [Fact]
        public async Task DeletingValueProviderFactory_AtResourceFilter_IsNotAvailableInPageContext()
        {
            // Arrange
            var resourceFilter = new Mock<IResourceFilter>();
            resourceFilter
                .Setup(f => f.OnResourceExecuting(It.IsAny<ResourceExecutingContext>()))
                .Callback<ResourceExecutingContext>((resourceExecutingContext) =>
                {
                    resourceExecutingContext.ValueProviderFactories.RemoveAt(0);
                });

            var valueProviderFactory1 = Mock.Of<IValueProviderFactory>();
            var valueProviderFactory2 = Mock.Of<IValueProviderFactory>();
            var valueProviderFactories = new List<IValueProviderFactory>
            {
                valueProviderFactory1,
                valueProviderFactory2
            };

            var invoker = CreateInvoker(
                new IFilterMetadata[] { resourceFilter.Object }, valueProviderFactories: valueProviderFactories);

            // Act
            await invoker.InvokeAsync();

            // Assert
            var pageContext = Assert.IsType<PageActionInvoker>(invoker).PageContext;
            Assert.NotNull(pageContext);
            Assert.Equal(1, pageContext.ValueProviderFactories.Count);
            Assert.Same(valueProviderFactory2, pageContext.ValueProviderFactories[0]);
        }

        #endregion

        #region Page vs PageModel

        [Fact]
        public async Task InvokeAction_WithSimplePage_FlowsRightValues()
        {
            // Arrange
            object instance = null;
            IActionResult result = null;

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c =>
                {
                    instance = c.HandlerInstance;
                });
            pageFilter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    Assert.Same(instance, c.HandlerInstance);
                });

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter
                .Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()))
                .Callback<ResultExecutingContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    result = c.Result;
                });
            resultFilter
                .Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()))
                .Callback<ResultExecutedContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    Assert.Same(result, c.Result);
                });

            var filters = new IFilterMetadata[] { pageFilter.Object, resultFilter.Object };

            var invoker = CreateInvoker(filters, CreateDescriptorForSimplePage());

            // Act
            await invoker.InvokeAsync();

            // Assert
            var page = Assert.IsType<TestPage>(instance);
            Assert.IsType<ViewDataDictionary<TestPage>>(page.ViewContext.ViewData);
            Assert.Same(page, page.ViewContext.ViewData.Model);

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(page, pageResult.Page);
            Assert.Same(page, pageResult.Model);
            Assert.Same(page.ViewContext.ViewData, pageResult.ViewData);
        }

        [Fact]
        public async Task InvokeAction_WithSimplePageWithPocoModel_FlowsRightValues()
        {
            // Arrange
            object instance = null;
            IActionResult result = null;

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c =>
                {
                    instance = c.HandlerInstance;
                });
            pageFilter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    Assert.Same(instance, c.HandlerInstance);
                });

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter
                .Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()))
                .Callback<ResultExecutingContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    result = c.Result;
                });
            resultFilter
                .Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()))
                .Callback<ResultExecutedContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    Assert.Same(result, c.Result);
                });

            var filters = new IFilterMetadata[] { pageFilter.Object, resultFilter.Object };

            var invoker = CreateInvoker(filters, CreateDescriptorForSimplePageWithPocoModel());

            // Act
            await invoker.InvokeAsync();

            // Assert
            var page = Assert.IsType<TestPage>(instance);
            Assert.IsType<ViewDataDictionary<PocoModel>>(page.PageContext.ViewData);
            Assert.Null(page.PageContext.ViewData.Model);

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(page, pageResult.Page);
            Assert.Null(pageResult.Model);
            Assert.Same(page.ViewContext.ViewData, pageResult.ViewData);

        }

        [Fact]
        public async Task InvokeAction_WithPageModel_FlowsRightValues()
        {
            // Arrange
            object instance = null;
            IActionResult result = null;

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c =>
                {
                    instance = c.HandlerInstance;
                });
            pageFilter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    Assert.Same(instance, c.HandlerInstance);
                });

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter
                .Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()))
                .Callback<ResultExecutingContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    result = c.Result;
                });
            resultFilter
                .Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()))
                .Callback<ResultExecutedContext>(c =>
                {
                    Assert.Same(instance, c.Controller);
                    Assert.Same(result, c.Result);
                });

            var filters = new IFilterMetadata[] { pageFilter.Object, resultFilter.Object };

            var invoker = CreateInvoker(
                filters,
                CreateDescriptorForPageModelPage(),
                modelFactory: context => new TestPageModel() { PageContext = context });

            // Act
            await invoker.InvokeAsync();

            // Assert
            var pageModel = Assert.IsType<TestPageModel>(instance);
            Assert.IsType<ViewDataDictionary<TestPageModel>>(pageModel.PageContext.ViewData);
            Assert.Same(pageModel, pageModel.ViewData.Model);

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.IsType<TestPage>(pageResult.Page);
            Assert.Same(pageModel, pageResult.Model);
            Assert.Same(pageModel.PageContext.ViewData, pageResult.ViewData);
        }

        [Fact]
        public async Task InvokeAction_WithPage_SetsExecutingFilePath()
        {
            // Arrange
            var relativePath = "/Pages/Users/Show.cshtml";
            var descriptor = CreateDescriptorForSimplePage();
            descriptor.RelativePath = relativePath;

            object instance = null;
            var pageFilter = new Mock<IPageFilter>();
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c =>
                {
                    instance = c.HandlerInstance;
                });
            var invoker = CreateInvoker(new[] { pageFilter.Object }, descriptor);

            // Act
            await invoker.InvokeAsync();

            // Assert
            var page = Assert.IsType<TestPage>(instance);
            Assert.Equal(relativePath, page.ViewContext.ExecutingFilePath);
        }
        #endregion

        #region Handler Selection

        [Fact]
        public async Task InvokeAction_InvokesPageFilter_CanModifySelectedHandler()
        {
            // Arrange
            HandlerMethodDescriptor handler = null;

            var filter1 = new Mock<IPageFilter>(MockBehavior.Strict);
            filter1
                .Setup(f => f.OnPageHandlerSelected(It.IsAny<PageHandlerSelectedContext>()))
                .Callback<PageHandlerSelectedContext>(c =>
                {
                    handler = c.HandlerMethod = c.ActionDescriptor.HandlerMethods[1];
                })
                .Verifiable();
            filter1
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Verifiable();
            filter1
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Verifiable();

            var filter2 = new Mock<IPageFilter>(MockBehavior.Strict);
            filter2
                .Setup(f => f.OnPageHandlerSelected(It.IsAny<PageHandlerSelectedContext>()))
                .Callback<PageHandlerSelectedContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Verifiable();
            filter2
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Verifiable();
            filter2
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Verifiable();

            var filters = new IFilterMetadata[] { filter1.Object, filter2.Object };

            var invoker = CreateInvoker(filters, actionDescriptor: CreateDescriptorForSimplePage());

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter1.Verify(f => f.OnPageHandlerSelected(It.IsAny<PageHandlerSelectedContext>()), Times.Once());
            filter1.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter1.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            filter2.Verify(f => f.OnPageHandlerSelected(It.IsAny<PageHandlerSelectedContext>()), Times.Once());
            filter2.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter2.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter_CanModifySelectedHandler()
        {
            // Arrange
            HandlerMethodDescriptor handler = null;

            var filter1 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            filter1
                .Setup(f => f.OnPageHandlerSelectionAsync(It.IsAny<PageHandlerSelectedContext>()))
                .Callback<PageHandlerSelectedContext>(c =>
                {
                    handler = c.HandlerMethod = c.ActionDescriptor.HandlerMethods[1];
                })
                .Returns(Task.CompletedTask)
                .Verifiable();
            filter1
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    Assert.Same(handler, c.HandlerMethod);
                    await next();
                })
                .Verifiable();

            var filter2 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            filter2
                .Setup(f => f.OnPageHandlerSelectionAsync(It.IsAny<PageHandlerSelectedContext>()))
                .Callback<PageHandlerSelectedContext>(c => Assert.Same(handler, c.HandlerMethod))
                .Returns(Task.CompletedTask)
                .Verifiable();
            filter2
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    Assert.Same(handler, c.HandlerMethod);
                    await next();
                })
                .Verifiable();

            var filters = new IFilterMetadata[] { filter1.Object, filter2.Object };

            var invoker = CreateInvoker(filters, actionDescriptor: CreateDescriptorForSimplePage());

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter1.Verify(f => f.OnPageHandlerSelectionAsync(It.IsAny<PageHandlerSelectedContext>()), Times.Once());
            filter1.Verify(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()), Times.Once());

            filter2.Verify(f => f.OnPageHandlerSelectionAsync(It.IsAny<PageHandlerSelectedContext>()), Times.Once());
            filter2.Verify(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()), Times.Once());
        }

        #endregion

        #region Page Filters

        [Fact]
        public async Task ViewDataIsSet_AfterHandlerMethodIsExecuted()
        {
            // Arrange
            var pageHandlerExecutedCalled = false;
            var pageFilter = new Mock<IPageFilter>();
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    pageHandlerExecutedCalled = true;
                    var result = c.Result;
                    var pageResult = Assert.IsType<PageResult>(result);
                    Assert.IsType<ViewDataDictionary<TestPage>>(pageResult.ViewData);
                    Assert.IsType<TestPage>(pageResult.Model);
                    Assert.Null(pageResult.Page);
                });
            var invoker = CreateInvoker(new IFilterMetadata[] { pageFilter.Object }, result: new PageResult());

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.True(pageHandlerExecutedCalled);
        }

        [Fact]
        public async Task InvokeAction_InvokesPageFilter()
        {
            // Arrange
            IActionResult result = null;

            var filter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter);
            filter.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            filter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => result = c.Result)
                .Verifiable();

            var invoker = CreateInvoker(filter.Object, result: Result);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            Assert.Same(Result, result);
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter()
        {
            // Arrange
            IActionResult result = null;

            var filter = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(filter);
            filter
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (context, next) =>
                {
                    var resultContext = await next();
                    result = resultContext.Result;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object, result: Result);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()),
                Times.Once());

            Assert.Same(Result, result);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_AsyncAuthorizeFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IAsyncAuthorizationFilter>(MockBehavior.Strict);
            filter
                .Setup(f => f.OnAuthorizationAsync(It.IsAny<AuthorizationFilterContext>()))
                .Returns<AuthorizationFilterContext>((context) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                    return Task.CompletedTask;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnAuthorizationAsync(It.IsAny<AuthorizationFilterContext>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_SyncAuthorizeFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IAuthorizationFilter>(MockBehavior.Strict);
            filter
                .Setup(f => f.OnAuthorization(It.IsAny<AuthorizationFilterContext>()))
                .Callback<AuthorizationFilterContext>((context) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnAuthorization(It.IsAny<AuthorizationFilterContext>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_AsyncResourceFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IAsyncResourceFilter>(MockBehavior.Strict);
            filter
                .Setup(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()))
                .Returns<ResourceExecutingContext, ResourceExecutionDelegate>((context, next) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                    return Task.CompletedTask;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_SyncResourceFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IResourceFilter>(MockBehavior.Strict);
            filter
                .Setup(f => f.OnResourceExecuting(It.IsAny<ResourceExecutingContext>()))
                .Callback<ResourceExecutingContext>((context) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnResourceExecuting(It.IsAny<ResourceExecutingContext>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_AsyncResultFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IAsyncResultFilter>(MockBehavior.Strict);
            filter
                .Setup(f => f.OnResultExecutionAsync(It.IsAny<ResultExecutingContext>(), It.IsAny<ResultExecutionDelegate>()))
                .Returns<ResultExecutingContext, ResultExecutionDelegate>((context, next) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                    return next();
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnResultExecutionAsync(It.IsAny<ResultExecutingContext>(), It.IsAny<ResultExecutionDelegate>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_SyncResultFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IResultFilter>();
            filter
                .Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()))
                .Callback<ResultExecutingContext>((context) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_AsyncPageFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(filter);
            filter
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>((context, next) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                    return Task.CompletedTask;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_PageResultSetAt_SyncPageFilter_PopulatesProperties()
        {
            // Arrange
            var expectedResult = new PageResult();

            IActionResult result = null;
            var filter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter);
            filter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>((context) =>
                {
                    context.Result = expectedResult;
                    result = context.Result;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(
                f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()),
                Times.Once());

            var pageResult = Assert.IsType<PageResult>(result);
            Assert.Same(expectedResult, pageResult);
            Assert.NotNull(pageResult.Page);
            Assert.NotNull(pageResult.ViewData);
            Assert.NotNull(pageResult.Page.ViewContext);
        }

        [Fact]
        public async Task InvokeAction_InvokesPageFilter_ShortCircuit()
        {
            // Arrange
            var result = new Mock<IActionResult>(MockBehavior.Strict);
            result
                .Setup(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()))
                .Returns(Task.FromResult(true))
                .Verifiable();

            PageHandlerExecutedContext context = null;

            var pageFilter1 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter1);
            pageFilter1.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            pageFilter1
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => context = c)
                .Verifiable();

            var pageFilter2 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter2);
            pageFilter2
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c => c.Result = result.Object)
                .Verifiable();

            var pageFilter3 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter3);

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter.Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>())).Verifiable();
            resultFilter.Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>())).Verifiable();

            var invoker = CreateInvoker(new IFilterMetadata[]
            {
                pageFilter1.Object,
                pageFilter2.Object,
                pageFilter3.Object,
                resultFilter.Object,
            });

            // Act
            await invoker.InvokeAsync();

            // Assert
            result.Verify(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()), Times.Once());
            pageFilter1.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            pageFilter1.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            pageFilter2.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            pageFilter2.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Never());

            resultFilter.Verify(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()), Times.Once());
            resultFilter.Verify(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()), Times.Once());

            Assert.True(context.Canceled);
            Assert.Same(context.Result, result.Object);
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter_ShortCircuit_WithResult()
        {
            // Arrange
            var result = new Mock<IActionResult>(MockBehavior.Strict);
            result
                .Setup(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()))
                .Returns(Task.FromResult(true))
                .Verifiable();

            PageHandlerExecutedContext context = null;

            var pageFilter1 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter1);
            pageFilter1.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            pageFilter1
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => context = c)
                .Verifiable();

            var pageFilter2 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter2);
            pageFilter2
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>((c, next) =>
                {
                    // Notice we're not calling next
                    c.Result = result.Object;
                    return Task.FromResult(true);
                })
                .Verifiable();

            var pageFilter3 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter3);

            var resultFilter1 = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter1.Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>())).Verifiable();
            resultFilter1.Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>())).Verifiable();
            var resultFilter2 = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter2.Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>())).Verifiable();
            resultFilter2.Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>())).Verifiable();

            var invoker = CreateInvoker(new IFilterMetadata[]
            {
                pageFilter1.Object,
                pageFilter2.Object,
                pageFilter3.Object,
                resultFilter1.Object,
                resultFilter2.Object,
            });

            // Act
            await invoker.InvokeAsync();

            // Assert
            result.Verify(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()), Times.Once());
            pageFilter1.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            pageFilter1.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            pageFilter2.Verify(
                f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()),
                Times.Once());

            resultFilter1.Verify(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()), Times.Once());
            resultFilter1.Verify(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()), Times.Once());
            resultFilter2.Verify(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()), Times.Once());
            resultFilter2.Verify(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()), Times.Once());

            Assert.True(context.Canceled);
            Assert.Same(context.Result, result.Object);
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter_ShortCircuit_WithoutResult()
        {
            // Arrange
            PageHandlerExecutedContext context = null;

            var pageFilter1 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter1);
            pageFilter1.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            pageFilter1
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => context = c)
                .Verifiable();

            var pageFilter2 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter2);
            pageFilter2
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>((c, next) =>
                {
                    // Notice we're not calling next
                    return Task.FromResult(true);
                })
                .Verifiable();

            var pageFilter3 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter3);

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter.Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>())).Verifiable();
            resultFilter.Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>())).Verifiable();

            var invoker = CreateInvoker(new IFilterMetadata[]
            {
                pageFilter1.Object,
                pageFilter2.Object,
                pageFilter3.Object,
                resultFilter.Object,
            });

            // Act
            await invoker.InvokeAsync();

            // Assert
            pageFilter1.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            pageFilter1.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            pageFilter2.Verify(
                f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()),
                Times.Once());

            resultFilter.Verify(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()), Times.Once());
            resultFilter.Verify(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()), Times.Once());

            Assert.True(context.Canceled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter_ShortCircuit_WithResult_CallNext()
        {
            // Arrange
            var pageFilter = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    c.Result = new EmptyResult();
                    await next();
                })
                .Verifiable();

            var message =
                "If an IAsyncPageFilter provides a result value by setting the Result property of " +
                "PageHandlerExecutingContext to a non-null value, then it cannot call the next filter by invoking " +
                "PageHandlerExecutionDelegate.";

            var invoker = CreateInvoker(pageFilter.Object);

            // Act & Assert
            await ExceptionAssert.ThrowsAsync<InvalidOperationException>(
                () => invoker.InvokeAsync(),
                message);
        }

        [Fact]
        public async Task InvokeAction_InvokesPageFilter_WithExceptionThrownByAction()
        {
            // Arrange
            PageHandlerExecutedContext context = null;

            var filter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter);
            filter.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            filter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    context = c;

                    // Handle the exception so the test doesn't throw.
                    Assert.Same(Exception, c.Exception);
                    Assert.False(c.ExceptionHandled);
                    c.ExceptionHandled = true;
                })
                .Verifiable();

            var invoker = CreateInvoker(filter.Object, exception: Exception);

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            Assert.Same(Exception, context.Exception);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task InvokeAction_InvokesPageFilter_WithExceptionThrownByPageFilter()
        {
            // Arrange
            var exception = new DataMisalignedException();
            PageHandlerExecutedContext context = null;

            var filter1 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter1);
            filter1.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            filter1
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    context = c;

                    // Handle the exception so the test doesn't throw.
                    Assert.False(c.ExceptionHandled);
                    c.ExceptionHandled = true;
                })
                .Verifiable();

            var filter2 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter2);
            filter2
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>(c => { throw exception; })
                .Verifiable();

            var invoker = CreateInvoker(new[] { filter1.Object, filter2.Object });

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter1.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter1.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            filter2.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            filter2.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Never());

            Assert.Same(exception, context.Exception);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncPageFilter_WithExceptionThrownByPageFilter()
        {
            // Arrange
            var exception = new DataMisalignedException();
            PageHandlerExecutedContext context = null;

            var filter1 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(filter1);
            filter1
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    context = await next();

                    // Handle the exception so the test doesn't throw.
                    Assert.False(context.ExceptionHandled);
                    context.ExceptionHandled = true;
                })
                .Verifiable();

            var filter2 = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(filter2);
            filter2.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            filter2
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c => { throw exception; })
                .Verifiable();

            var invoker = CreateInvoker(new IFilterMetadata[] { filter1.Object, filter2.Object });

            // Act
            await invoker.InvokeAsync();

            // Assert
            filter1.Verify(
                f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()),
                Times.Once());

            filter2.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());

            Assert.Same(exception, context.Exception);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task InvokeAction_InvokesPageFilter_HandleException()
        {
            // Arrange
            var result = new Mock<IActionResult>(MockBehavior.Strict);
            result
                .Setup(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()))
                .Returns<ActionContext>((context) => Task.FromResult(true))
                .Verifiable();

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter.Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>())).Verifiable();
            pageFilter
                .Setup(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()))
                .Callback<PageHandlerExecutedContext>(c =>
                {
                    // Handle the exception so the test doesn't throw.
                    Assert.False(c.ExceptionHandled);
                    c.ExceptionHandled = true;

                    c.Result = result.Object;
                })
                .Verifiable();

            var resultFilter = new Mock<IResultFilter>(MockBehavior.Strict);
            resultFilter.Setup(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>())).Verifiable();
            resultFilter.Setup(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>())).Verifiable();

            var invoker = CreateInvoker(
                new IFilterMetadata[] { pageFilter.Object, resultFilter.Object },
                exception: Exception);

            // Act
            await invoker.InvokeAsync();

            // Assert
            pageFilter.Verify(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()), Times.Once());
            pageFilter.Verify(f => f.OnPageHandlerExecuted(It.IsAny<PageHandlerExecutedContext>()), Times.Once());

            resultFilter.Verify(f => f.OnResultExecuting(It.IsAny<ResultExecutingContext>()), Times.Once());
            resultFilter.Verify(f => f.OnResultExecuted(It.IsAny<ResultExecutedContext>()), Times.Once());

            result.Verify(r => r.ExecuteResultAsync(It.IsAny<ActionContext>()), Times.Once());
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncResourceFilter_WithActionResult_FromPageFilter()
        {
            // Arrange
            var expected = Mock.Of<IActionResult>();

            ResourceExecutedContext context = null;
            var resourceFilter = new Mock<IAsyncResourceFilter>(MockBehavior.Strict);
            resourceFilter
                .Setup(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()))
                .Returns<ResourceExecutingContext, ResourceExecutionDelegate>(async (c, next) =>
                {
                    context = await next();
                })
                .Verifiable();

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>((c) =>
                {
                    c.Result = expected;
                });

            var invoker = CreateInvoker(new IFilterMetadata[] { resourceFilter.Object, pageFilter.Object });

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.Same(expected, context.Result);

            resourceFilter.Verify(
                f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()),
                Times.Once());
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncResourceFilter_HandleException_FromPageFilter()
        {
            // Arrange
            var expected = new DataMisalignedException();

            ResourceExecutedContext context = null;
            var resourceFilter = new Mock<IAsyncResourceFilter>(MockBehavior.Strict);
            resourceFilter
                .Setup(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()))
                .Returns<ResourceExecutingContext, ResourceExecutionDelegate>(async (c, next) =>
                {
                    context = await next();
                    context.ExceptionHandled = true;
                })
                .Verifiable();

            var pageFilter = new Mock<IPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter);
            pageFilter
                .Setup(f => f.OnPageHandlerExecuting(It.IsAny<PageHandlerExecutingContext>()))
                .Callback<PageHandlerExecutingContext>((c) =>
                {
                    throw expected;
                });

            var invoker = CreateInvoker(new IFilterMetadata[] { resourceFilter.Object, pageFilter.Object });

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.Same(expected, context.Exception);
            Assert.Same(expected, context.ExceptionDispatchInfo.SourceException);

            resourceFilter.Verify(
                f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()),
                Times.Once());
        }

        [Fact]
        public async Task InvokeAction_InvokesAsyncResourceFilter_HandlesException_FromExceptionFilter()
        {
            // Arrange
            var expected = new DataMisalignedException();

            ResourceExecutedContext context = null;
            var resourceFilter = new Mock<IAsyncResourceFilter>(MockBehavior.Strict);
            resourceFilter
                .Setup(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()))
                .Returns<ResourceExecutingContext, ResourceExecutionDelegate>(async (c, next) =>
                {
                    context = await next();
                    context.ExceptionHandled = true;
                })
                .Verifiable();

            var exceptionFilter = new Mock<IExceptionFilter>(MockBehavior.Strict);
            exceptionFilter
                .Setup(f => f.OnException(It.IsAny<ExceptionContext>()))
                .Callback<ExceptionContext>((c) =>
                {
                    throw expected;
                });

            var invoker = CreateInvoker(new IFilterMetadata[] { resourceFilter.Object, exceptionFilter.Object }, exception: Exception);

            // Act
            await invoker.InvokeAsync();

            // Assert
            Assert.Same(expected, context.Exception);
            Assert.Same(expected, context.ExceptionDispatchInfo.SourceException);

            resourceFilter.Verify(
                f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()),
                Times.Once());
        }

        [Fact]
        public async Task InvokeAction_ExceptionBubbling_AsyncPageFilter_To_ResourceFilter()
        {
            // Arrange
            var resourceFilter = new Mock<IAsyncResourceFilter>(MockBehavior.Strict);
            resourceFilter
                .Setup(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()))
                .Returns<ResourceExecutingContext, ResourceExecutionDelegate>(async (c, next) =>
                {
                    var context = await next();
                    Assert.Same(Exception, context.Exception);
                    context.ExceptionHandled = true;
                })
                .Verifiable();

            var pageFilter1 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter1);
            pageFilter1
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    await next();
                });

            var pageFilter2 = new Mock<IAsyncPageFilter>(MockBehavior.Strict);
            AllowSelector(pageFilter2);
            pageFilter2
                .Setup(f => f.OnPageHandlerExecutionAsync(It.IsAny<PageHandlerExecutingContext>(), It.IsAny<PageHandlerExecutionDelegate>()))
                .Returns<PageHandlerExecutingContext, PageHandlerExecutionDelegate>(async (c, next) =>
                {
                    await next();
                });

            var invoker = CreateInvoker(
                new IFilterMetadata[]
                {
                    resourceFilter.Object,
                    pageFilter1.Object,
                    pageFilter2.Object,
                },
                // The action won't run
                exception: Exception);

            // Act & Assert
            await invoker.InvokeAsync();

            resourceFilter.Verify(f => f.OnResourceExecutionAsync(It.IsAny<ResourceExecutingContext>(), It.IsAny<ResourceExecutionDelegate>()), Times.Once());
        }

        #endregion

        protected override ResourceInvoker CreateInvoker(
            IFilterMetadata[] filters,
            Exception exception = null,
            IActionResult result = null,
            IList<IValueProviderFactory> valueProviderFactories = null)
        {
            var actionDescriptor = new CompiledPageActionDescriptor
            {
                ViewEnginePath = "/Index.cshtml",
                RelativePath = "/Index.cshtml",
                HandlerMethods = new List<HandlerMethodDescriptor>(),
                HandlerTypeInfo = typeof(TestPage).GetTypeInfo(),
                ModelTypeInfo = typeof(TestPage).GetTypeInfo(),
                PageTypeInfo = typeof(TestPage).GetTypeInfo(),
                BoundProperties = new List<ParameterDescriptor>(),
            };

            var handlers = new List<PageHandlerExecutorDelegate>();
            if (result != null)
            {
                handlers.Add((obj, args) => Task.FromResult(result));
                actionDescriptor.HandlerMethods.Add(new HandlerMethodDescriptor()
                {
                    HttpMethod = "GET",
                    Parameters = new List<HandlerParameterDescriptor>(),
                });
            }
            else if (exception != null)
            {
                handlers.Add((obj, args) => Task.FromException<IActionResult>(exception));
                actionDescriptor.HandlerMethods.Add(new HandlerMethodDescriptor()
                {
                    HttpMethod = "GET",
                    Parameters = new List<HandlerParameterDescriptor>(),
                });
            }

            return CreateInvoker(
                filters,
                actionDescriptor,
                handlers: handlers.ToArray(),
                valueProviderFactories: valueProviderFactories);
        }

        private PageActionInvoker CreateInvoker(
            IFilterMetadata[] filters,
            CompiledPageActionDescriptor actionDescriptor,
            Func<PageContext, object> modelFactory = null,
            ITempDataDictionaryFactory tempDataFactory = null,
            IList<IValueProviderFactory> valueProviderFactories = null,
            PageHandlerExecutorDelegate[] handlers = null,
            PageHandlerBinderDelegate[] handlerBinders = null,
            RouteData routeData = null,
            ILogger logger = null,
            TestDiagnosticListener listener = null)
        {
            var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");
            if (listener != null)
            {
                diagnosticListener.SubscribeWithAdapter(listener);
            }

            var httpContext = new DefaultHttpContext();
            var services = new ServiceCollection();
            services.AddSingleton<PageResultExecutor, TestPageResultExecutor>();
            httpContext.RequestServices = services.BuildServiceProvider();

            var pageContext = new PageContext()
            {
                ActionDescriptor = actionDescriptor,
                HttpContext = httpContext,
                RouteData = routeData ?? new RouteData(),
                ValueProviderFactories = valueProviderFactories?.ToList() ?? new List<IValueProviderFactory>(),
                ViewStartFactories = new List<Func<IRazorPage>>(),
            };

            var viewDataFactory = ViewDataDictionaryFactory.CreateFactory(actionDescriptor.ModelTypeInfo);
            pageContext.ViewData = viewDataFactory(new EmptyModelMetadataProvider(), pageContext.ModelState);

            if (tempDataFactory == null)
            {
                tempDataFactory = Mock.Of<ITempDataDictionaryFactory>(m => m.GetTempData(It.IsAny<HttpContext>()) == Mock.Of<ITempDataDictionary>());
            }

            object pageFactory(PageContext context, ViewContext viewContext)
            {
                var instance = (Page)Activator.CreateInstance(actionDescriptor.PageTypeInfo.AsType());
                instance.PageContext = context;
                instance.ViewContext = viewContext;
                return instance;
            }

            if (handlers == null)
            {
                handlers = new PageHandlerExecutorDelegate[actionDescriptor.HandlerMethods.Count];
                for (var i = 0; i < handlers.Length; i++)
                {
                    handlers[i] = (obj, args) => Task.FromResult<IActionResult>(new PageResult());
                }
            }

            handlerBinders = handlerBinders ?? Array.Empty<PageHandlerBinderDelegate>();

            if (modelFactory == null)
            {
                modelFactory = _ => Activator.CreateInstance(actionDescriptor.ModelTypeInfo.AsType());
            }

            var cacheEntry = new PageActionInvokerCacheEntry(
                actionDescriptor,
                viewDataFactory,
                pageFactory,
                (c, viewContext, page) => { (page as IDisposable)?.Dispose(); },
                modelFactory,
                (c, model) => { (model as IDisposable)?.Dispose(); },
                null,
                handlers,
                handlerBinders,
                null,
                new FilterItem[0]);

            // Always just select the first one.
            var selector = new Mock<IPageHandlerMethodSelector>();
            selector
                .Setup(s => s.Select(It.IsAny<PageContext>()))
                .Returns<PageContext>(c => c.ActionDescriptor.HandlerMethods.FirstOrDefault());

            var invoker = new PageActionInvoker(
                selector.Object,
                diagnosticListener ?? new DiagnosticListener("Microsoft.AspNetCore"),
                logger ?? NullLogger.Instance,
                new ActionResultTypeMapper(),
                pageContext,
                filters ?? Array.Empty<IFilterMetadata>(),
                cacheEntry,
                GetParameterBinder(),
                tempDataFactory,
                new HtmlHelperOptions());
            return invoker;
        }

        private static ParameterBinder GetParameterBinder(
            IModelBinderFactory factory = null,
            IModelValidatorProvider validator = null)
        {
            if (validator == null)
            {
                validator = CreateMockValidatorProvider();
            }

            if (factory == null)
            {
                factory = TestModelBinderFactory.CreateDefault();
            }

            var metadataProvider = TestModelMetadataProvider.CreateDefaultProvider();
            var mvcOptions = new MvcOptions
            {
                AllowValidatingTopLevelNodes = true,
            };

            return new ParameterBinder(
                metadataProvider,
                factory,
                new DefaultObjectValidator(metadataProvider, new[] { validator }),
                Options.Create(mvcOptions),
                NullLoggerFactory.Instance);
        }

        private static IModelValidatorProvider CreateMockValidatorProvider()
        {
            var mockValidator = new Mock<IModelValidatorProvider>(MockBehavior.Strict);
            mockValidator
                .Setup(o => o.CreateValidators(
                    It.IsAny<ModelValidatorProviderContext>()));
            return mockValidator.Object;
        }

        private CompiledPageActionDescriptor CreateDescriptorForSimplePage()
        {
            return new CompiledPageActionDescriptor()
            {
                HandlerTypeInfo = typeof(TestPage).GetTypeInfo(),
                ModelTypeInfo = typeof(TestPage).GetTypeInfo(),
                PageTypeInfo = typeof(TestPage).GetTypeInfo(),

                BoundProperties = new List<ParameterDescriptor>(),

                HandlerMethods = new HandlerMethodDescriptor[]
                {
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(TestPage).GetTypeInfo().GetMethod(nameof(TestPage.OnGetHandler1)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(TestPage).GetTypeInfo().GetMethod(nameof(TestPage.OnGetHandler2)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                },
            };
        }

        private CompiledPageActionDescriptor CreateDescriptorForSimplePageWithPocoModel()
        {
            return new CompiledPageActionDescriptor()
            {
                HandlerTypeInfo = typeof(TestPage).GetTypeInfo(),
                ModelTypeInfo = typeof(PocoModel).GetTypeInfo(),
                PageTypeInfo = typeof(TestPage).GetTypeInfo(),

                BoundProperties = new List<ParameterDescriptor>(),

                HandlerMethods = new HandlerMethodDescriptor[]
                {
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(TestPage).GetTypeInfo().GetMethod(nameof(TestPage.OnGetHandler1)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(TestPage).GetTypeInfo().GetMethod(nameof(TestPage.OnGetHandler2)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                },
            };
        }

        private CompiledPageActionDescriptor CreateDescriptorForPageModelPage()
        {
            return new CompiledPageActionDescriptor()
            {
                HandlerTypeInfo = typeof(TestPageModel).GetTypeInfo(),
                ModelTypeInfo = typeof(TestPageModel).GetTypeInfo(),
                PageTypeInfo = typeof(TestPage).GetTypeInfo(),

                BoundProperties = new List<ParameterDescriptor>(),

                HandlerMethods = new HandlerMethodDescriptor[]
                {
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(PageModel).GetTypeInfo().GetMethod(nameof(TestPageModel.OnGetHandler1)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                    new HandlerMethodDescriptor()
                    {
                        HttpMethod = "GET",
                        MethodInfo = typeof(PageModel).GetTypeInfo().GetMethod(nameof(TestPageModel.OnGetHandler2)),
                        Parameters = new List<HandlerParameterDescriptor>(),
                    },
                },
            };
        }

        private void AllowSelector(Mock<IPageFilter> filter)
        {
            filter.Setup(f => f.OnPageHandlerSelected(It.IsAny<PageHandlerSelectedContext>()));
        }

        private void AllowSelector(Mock<IAsyncPageFilter> filter)
        {
            filter.Setup(f => f.OnPageHandlerSelectionAsync(It.IsAny<PageHandlerSelectedContext>())).Returns(Task.CompletedTask);
        }

        private class TestPageResultExecutor : PageResultExecutor
        {
            private readonly Func<PageContext, Task> _executeAction;

            public TestPageResultExecutor()
                : this(null)
            {
            }

            public TestPageResultExecutor(Func<PageContext, Task> executeAction)
                : base(
                    Mock.Of<IHttpResponseStreamWriterFactory>(),
                    Mock.Of<ICompositeViewEngine>(),
                    Mock.Of<IRazorViewEngine>(),
                    Mock.Of<IRazorPageActivator>(),
                    new DiagnosticListener("Microsoft.AspNetCore"),
                    HtmlEncoder.Default)
            {
                _executeAction = executeAction;
            }

            public override Task ExecuteAsync(PageContext pageContext, PageResult result)
            {
                return _executeAction?.Invoke(pageContext) ?? Task.CompletedTask;
            }
        }

        private class PocoModel
        {
        }

        private class TestPage : Page
        {
            public void OnGetHandler1()
            {
            }

            public void OnGetHandler2()
            {
            }

            public override Task ExecuteAsync()
            {
                throw new NotImplementedException();
            }
        }

        private class TestPageModel : PageModel
        {
            public void OnGetHandler1()
            {
            }

            public void OnGetHandler2()
            {
            }
        }
    }
}
