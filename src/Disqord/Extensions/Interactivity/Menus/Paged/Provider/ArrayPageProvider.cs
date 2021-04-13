﻿using System;
using System.Linq;
using System.Threading.Tasks;

namespace Disqord.Extensions.Interactivity.Menus.Paged
{
    /// <summary>
    ///     Represents a method that formats a given item into a <see cref="Page"/>.
    /// </summary>
    /// <typeparam name="T"> The type of the item. </typeparam>
    /// <param name="menu"> The <see cref="PagedMenu"/> to format the item for. </param>
    /// <param name="item"> The <typeparamref name="T"/> item to format. </param>
    /// <returns>
    ///     The formatted item as a <see cref="Page"/>.
    /// </returns>
    public delegate Page PageFormatter<T>(PagedMenu menu, T item);

    /// <summary>
    ///     Creates pages automatically from an array of items.
    /// </summary>
    /// <typeparam name="T"> The type of items in the array. </typeparam>
    public class ArrayPageProvider<T> : IPageProvider
    {
        /// <summary>
        ///     Gets the array of items of this provider.
        /// </summary>
        public T[] Array { get; }

        /// <summary>
        ///     Gets the amount of items per-<see cref="Page"/> of this provider.
        /// </summary>
        public int ItemsPerPage { get; }

        /// <summary>
        ///     Gets the <see cref="PageFormatter{T}"/> of this provider.
        /// </summary>
        public PageFormatter<ArraySegment<T>> Formatter { get; }

        /// <inheritdoc/>
        public int PageCount => (int) Math.Ceiling(Array.Length / (double) ItemsPerPage);

        /// <summary>
        ///     Instantiates a new <see cref="ArrayPageProvider{T}"/> with the specified array of items
        ///     and optionally the <see cref="PageFormatter{T}"/> and a number of items per <see cref="Page"/>.
        /// </summary>
        /// <param name="array"> The array of items. </param>
        /// <param name="formatter"> The <see cref="PageFormatter{T}"/>. If <see langword="null"/>, defaults to <see cref="DefaultFormatter"/>. </param>
        /// <param name="itemsPerPage"> The number of items per-<see cref="Page"/>. </param>
        public ArrayPageProvider(T[] array, PageFormatter<ArraySegment<T>> formatter = null, int itemsPerPage = 10)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (itemsPerPage <= 0 || itemsPerPage > array.Length)
                throw new ArgumentOutOfRangeException(nameof(itemsPerPage));

            Array = array;
            ItemsPerPage = itemsPerPage;
            Formatter = formatter ?? DefaultFormatter;
        }

        /// <inheritdoc/>
        public virtual ValueTask<Page> GetPageAsync(PagedMenu menu)
        {
            var offset = menu.CurrentPageIndex * ItemsPerPage;
            var remainder = Array.Length - offset;
            var segment = new ArraySegment<T>(Array, offset, ItemsPerPage > remainder
                ? remainder
                : ItemsPerPage);
            var page = Formatter(menu, segment);
            return new(page);
        }

        public static readonly PageFormatter<ArraySegment<T>> DefaultFormatter = static (menu, segment) =>
        {
            var description = string.Join('\n', segment.Select((x, i) =>
            {
                var pageProvider = menu.PageProvider as ArrayPageProvider<T>;
                var itemPrefix = $"{i + segment.Offset + 1}. ";
                var maxItemLength = (int) Math.Floor((double) LocalEmbedBuilder.MAX_DESCRIPTION_LENGTH / pageProvider.ItemsPerPage) - itemPrefix.Length - 2;
                if (maxItemLength <= 0)
                    throw new InvalidOperationException("There are too many items per-page. Set a lower amount or provide a custom page formatter.");

                var item = x.ToString();
                if (item.Length > maxItemLength)
                    item = $"{item[0..maxItemLength]}…";

                return string.Concat(itemPrefix, item);
            }));
            return new LocalEmbedBuilder()
                .WithDescription(description)
                .WithFooter($"Page {menu.CurrentPageIndex + 1}/{menu.PageProvider.PageCount}");
        };
    }
}