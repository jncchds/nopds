import { useParams } from 'react-router'
import { useSeriesName } from '../api/hooks'
import { InfiniteBooks } from '../components/InfiniteBooks'
import { PageTitle } from '../components/ui'

export default function SeriesPage() {
  const { id } = useParams()
  const seriesId = Number(id)
  const series = useSeriesName(seriesId)
  return (
    <>
      <PageTitle>{series.data?.name ?? '…'}</PageTitle>
      <InfiniteBooks filter={{ series: seriesId, sort: 'seriesNumber', dupes: true }} />
    </>
  )
}
